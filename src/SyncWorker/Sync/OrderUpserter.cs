using Microsoft.EntityFrameworkCore;
using SyncWorker.Data;
using SyncWorker.Erp;

namespace SyncWorker.Sync;

public enum ItemOutcome
{
    Created,
    Updated,
    Skipped,
    Failed
}

/// <summary>
/// Aplica um pedido do ERP na base da loja. Usado tanto pela leitura incremental
/// quanto pelo reprocessamento, para que as duas sigam exatamente a mesma regra.
/// </summary>
public sealed class OrderUpserter(SyncDbContext db, TimeProvider clock, ILogger<OrderUpserter> logger)
{
    public const string Entity = "orders";

    public async Task<ItemOutcome> ApplyAsync(
        ErpOrderDto dto,
        IReadOnlyDictionary<string, string> statusMap,
        CancellationToken ct)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object> { ["ErpOrderId"] = dto.Id });

        var validationError = Validate(dto, statusMap);
        if (validationError is not null)
        {
            await RegisterErrorAsync(dto.Id, validationError, ct);
            logger.LogWarning("Pedido rejeitado: {Reason}", validationError);
            return ItemOutcome.Failed;
        }

        try
        {
            var existing = await db.Orders.SingleOrDefaultAsync(o => o.ErpOrderId == dto.Id, ct);

            // Idempotência: a mesma versão (ou uma mais antiga) não é regravada,
            // e o log de sucesso não é reescrito, preservando a data da integração.
            if (existing is not null && existing.ErpUpdatedAtUtc >= dto.UpdatedAtUtc)
            {
                // Se o item estava com erro (ex.: reprocesso de uma versão que já foi
                // gravada por outro caminho), fecha o erro sem tocar no pedido.
                if (await HasOpenErrorAsync(dto.Id, ct))
                {
                    await UpsertLogAsync(dto.Id, IntegrationStatus.Success, message: null, countAttempt: false, ct);
                    await db.SaveChangesAsync(ct);
                }

                return ItemOutcome.Skipped;
            }

            var now = clock.GetUtcNow().UtcDateTime;
            var storeStatus = statusMap[dto.Status];
            ItemOutcome outcome;

            if (existing is null)
            {
                db.Orders.Add(new StoreOrder
                {
                    ErpOrderId = dto.Id,
                    CustomerName = dto.Customer.Trim(),
                    Total = dto.Total,
                    Status = storeStatus,
                    ErpUpdatedAtUtc = dto.UpdatedAtUtc,
                    SyncedAtUtc = now
                });
                outcome = ItemOutcome.Created;
            }
            else
            {
                existing.CustomerName = dto.Customer.Trim();
                existing.Total = dto.Total;
                existing.Status = storeStatus;
                existing.ErpUpdatedAtUtc = dto.UpdatedAtUtc;
                existing.SyncedAtUtc = now;
                outcome = ItemOutcome.Updated;
            }

            await UpsertLogAsync(dto.Id, IntegrationStatus.Success, message: null, countAttempt: false, ct);
            await db.SaveChangesAsync(ct);
            return outcome;
        }
        catch (DbUpdateException ex)
        {
            // Descarta o que ficou pendente no contexto para não contaminar os próximos itens.
            db.ChangeTracker.Clear();
            logger.LogError(ex, "Falha ao gravar o pedido");
            await RegisterErrorAsync(dto.Id, $"Falha ao gravar: {ex.GetBaseException().Message}", ct);
            return ItemOutcome.Failed;
        }
    }

    /// <summary>Marca um item com erro, somando uma tentativa.</summary>
    public async Task RegisterErrorAsync(string externalId, string message, CancellationToken ct)
    {
        await UpsertLogAsync(externalId, IntegrationStatus.Error, message, countAttempt: true, ct);
        await db.SaveChangesAsync(ct);
    }

    internal static string? Validate(ErpOrderDto dto, IReadOnlyDictionary<string, string> statusMap)
    {
        if (string.IsNullOrWhiteSpace(dto.Id))
        {
            return "Pedido sem id.";
        }

        if (string.IsNullOrWhiteSpace(dto.Customer))
        {
            return "Pedido sem cliente.";
        }

        if (dto.Total < 0)
        {
            return $"Total negativo ({dto.Total}).";
        }

        if (!statusMap.ContainsKey(dto.Status))
        {
            return $"Status '{dto.Status}' sem de-para cadastrado.";
        }

        return null;
    }

    private Task<bool> HasOpenErrorAsync(string externalId, CancellationToken ct) =>
        db.IntegrationLogs.AnyAsync(
            l => l.Entity == Entity && l.ExternalId == externalId && l.Status != IntegrationStatus.Success,
            ct);

    private async Task UpsertLogAsync(
        string externalId,
        IntegrationStatus status,
        string? message,
        bool countAttempt,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var log = await db.IntegrationLogs
            .SingleOrDefaultAsync(l => l.Entity == Entity && l.ExternalId == externalId, ct);

        if (log is null)
        {
            log = new IntegrationLog
            {
                Entity = Entity,
                ExternalId = externalId,
                CreatedAtUtc = now
            };
            db.IntegrationLogs.Add(log);
        }

        log.Status = status;
        log.Message = message;
        log.UpdatedAtUtc = now;

        if (countAttempt)
        {
            log.Attempts++;
        }
        else if (status == IntegrationStatus.Success)
        {
            log.Attempts = 0;
        }
    }
}
