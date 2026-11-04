using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SyncWorker.Data;
using SyncWorker.Erp;

namespace SyncWorker.Sync;

public sealed record ReprocessResult(int Attempted, int Recovered, int StillFailing, int GaveUp);

/// <summary>
/// Reprocessa os itens que ficaram com erro: busca de novo no ERP, pelo id,
/// e aplica com a mesma regra da leitura incremental. Depois de N tentativas
/// o item é marcado como GaveUp e sai da fila (precisa de análise manual).
/// </summary>
public sealed class ReprocessService(
    SyncDbContext db,
    IErpClient erp,
    OrderUpserter upserter,
    IOptions<SyncOptions> options,
    ILogger<ReprocessService> logger)
{
    public async Task<ReprocessResult> RunAsync(CancellationToken ct)
    {
        var opt = options.Value;

        var pending = await db.IntegrationLogs
            .Where(l => l.Entity == OrderUpserter.Entity
                        && l.Status == IntegrationStatus.Error
                        && l.Attempts < opt.MaxReprocessAttempts)
            .OrderBy(l => l.UpdatedAtUtc)
            .Take(opt.ReprocessBatchSize)
            .Select(l => l.ExternalId)
            .ToListAsync(ct);

        if (pending.Count == 0)
        {
            return new ReprocessResult(0, 0, 0, 0);
        }

        var statusMap = await OrderSyncService.LoadStatusMapAsync(db, ct);
        int recovered = 0, stillFailing = 0, gaveUp = 0;

        foreach (var externalId in pending)
        {
            var dto = await erp.GetOrderAsync(externalId, ct);

            var outcome = dto is null
                ? await NotFoundAsync(externalId, ct)
                : await upserter.ApplyAsync(dto, statusMap, ct);

            if (outcome != ItemOutcome.Failed)
            {
                recovered++;
                continue;
            }

            if (await MarkGaveUpIfExhaustedAsync(externalId, opt.MaxReprocessAttempts, ct))
            {
                gaveUp++;
            }
            else
            {
                stillFailing++;
            }
        }

        var result = new ReprocessResult(pending.Count, recovered, stillFailing, gaveUp);
        logger.LogInformation(
            "Reprocesso: {Attempted} tentados, {Recovered} recuperados, {StillFailing} ainda com erro, {GaveUp} desistidos",
            result.Attempted, result.Recovered, result.StillFailing, result.GaveUp);

        return result;
    }

    private async Task<ItemOutcome> NotFoundAsync(string externalId, CancellationToken ct)
    {
        await upserter.RegisterErrorAsync(externalId, "Pedido não encontrado no ERP.", ct);
        return ItemOutcome.Failed;
    }

    private async Task<bool> MarkGaveUpIfExhaustedAsync(string externalId, int maxAttempts, CancellationToken ct)
    {
        var log = await db.IntegrationLogs
            .SingleAsync(l => l.Entity == OrderUpserter.Entity && l.ExternalId == externalId, ct);

        if (log.Attempts < maxAttempts)
        {
            return false;
        }

        log.Status = IntegrationStatus.GaveUp;
        await db.SaveChangesAsync(ct);
        logger.LogWarning("Pedido {ErpOrderId} atingiu {Max} tentativas e saiu da fila: {Message}",
            externalId, maxAttempts, log.Message);
        return true;
    }
}
