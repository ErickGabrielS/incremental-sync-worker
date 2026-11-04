using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SyncWorker.Data;
using SyncWorker.Erp;

namespace SyncWorker.Sync;

public sealed record SyncResult(int Read, int Created, int Updated, int Skipped, int Failed, DateTime WatermarkUtc);

/// <summary>
/// Leitura incremental: busca no ERP só o que mudou desde a última marca d'água,
/// página por página, e aplica cada pedido na base da loja.
/// </summary>
public sealed class OrderSyncService(
    SyncDbContext db,
    IErpClient erp,
    OrderUpserter upserter,
    IOptions<SyncOptions> options,
    TimeProvider clock,
    ILogger<OrderSyncService> logger)
{
    public async Task<SyncResult> RunAsync(CancellationToken ct)
    {
        var opt = options.Value;
        var watermark = await GetOrCreateWatermarkAsync(ct);
        var since = ComputeStart(watermark.LastUpdatedAtUtc, TimeSpan.FromMinutes(opt.OverlapMinutes));
        var statusMap = await LoadStatusMapAsync(db, ct);

        logger.LogInformation("Iniciando leitura incremental a partir de {Since:o}", since);

        int read = 0, created = 0, updated = 0, skipped = 0, failed = 0;
        var maxSeen = watermark.LastUpdatedAtUtc;
        var page = 1;
        PagedResult<ErpOrderDto> batch;

        do
        {
            batch = await erp.GetOrdersAsync(since, page, opt.PageSize, ct);

            foreach (var dto in batch.Items)
            {
                read++;
                switch (await upserter.ApplyAsync(dto, statusMap, ct))
                {
                    case ItemOutcome.Created: created++; break;
                    case ItemOutcome.Updated: updated++; break;
                    case ItemOutcome.Skipped: skipped++; break;
                    case ItemOutcome.Failed: failed++; break;
                }

                // A marca d'água avança mesmo com falha no item: quem falhou fica no
                // IntegrationLog e é buscado de novo pelo ReprocessService, pelo id.
                // Assim um pedido com problema não trava a fila inteira.
                if (dto.UpdatedAtUtc > maxSeen)
                {
                    maxSeen = dto.UpdatedAtUtc;
                }
            }

            page++;
        }
        while (batch.HasMore);

        watermark.LastUpdatedAtUtc = maxSeen;
        watermark.LastRunAtUtc = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);

        var result = new SyncResult(read, created, updated, skipped, failed, maxSeen);
        logger.LogInformation(
            "Leitura concluída: {Read} lidos, {Created} criados, {Updated} atualizados, {Skipped} sem alteração, {Failed} com erro. Marca d'água: {Watermark:o}",
            result.Read, result.Created, result.Updated, result.Skipped, result.Failed, result.WatermarkUtc);

        return result;
    }

    /// <summary>
    /// Início da janela de leitura: a marca d'água menos a margem de segurança.
    /// Na primeira execução, lê tudo.
    /// </summary>
    public static DateTime ComputeStart(DateTime watermarkUtc, TimeSpan overlap)
    {
        if (watermarkUtc == DateTime.MinValue || watermarkUtc - DateTime.MinValue < overlap)
        {
            return DateTime.MinValue;
        }

        return watermarkUtc - overlap;
    }

    internal static async Task<IReadOnlyDictionary<string, string>> LoadStatusMapAsync(SyncDbContext db, CancellationToken ct)
    {
        var mappings = await db.StatusMappings
            .Where(m => m.Active)
            .ToListAsync(ct);

        return mappings.ToDictionary(m => m.ErpStatus, m => m.StoreStatus, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<SyncWatermark> GetOrCreateWatermarkAsync(CancellationToken ct)
    {
        var watermark = await db.SyncWatermarks.SingleOrDefaultAsync(w => w.Name == OrderUpserter.Entity, ct);

        if (watermark is null)
        {
            watermark = new SyncWatermark { Name = OrderUpserter.Entity, LastUpdatedAtUtc = DateTime.MinValue };
            db.SyncWatermarks.Add(watermark);
        }

        return watermark;
    }
}
