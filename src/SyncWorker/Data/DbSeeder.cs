using Microsoft.EntityFrameworkCore;

namespace SyncWorker.Data;

public static class DbSeeder
{
    /// <summary>
    /// De-para inicial. "EM_ANALISE" fica de fora de propósito: o ERP fictício
    /// envia esse status de vez em quando para demonstrar o fluxo de erro e reprocesso.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> DefaultStatusMappings =
        new Dictionary<string, string>
        {
            ["ABERTO"] = "Pending",
            ["FATURADO"] = "Invoiced",
            ["ENVIADO"] = "Shipped",
            ["CANCELADO"] = "Cancelled"
        };

    public static async Task SeedStatusMappingsAsync(SyncDbContext db, CancellationToken ct = default)
    {
        var existing = await db.StatusMappings.Select(m => m.ErpStatus).ToListAsync(ct);

        foreach (var (erp, store) in DefaultStatusMappings)
        {
            if (!existing.Contains(erp))
            {
                db.StatusMappings.Add(new StatusMapping { ErpStatus = erp, StoreStatus = store });
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
