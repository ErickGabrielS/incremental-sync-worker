namespace SyncWorker.Data;

/// <summary>Pedido gravado na base da loja.</summary>
public sealed class StoreOrder
{
    public int Id { get; set; }
    public required string ErpOrderId { get; set; }
    public required string CustomerName { get; set; }
    public decimal Total { get; set; }
    public required string Status { get; set; }

    /// <summary>Data da última alteração no ERP. Usada para idempotência.</summary>
    public DateTime ErpUpdatedAtUtc { get; set; }

    public DateTime SyncedAtUtc { get; set; }
}

/// <summary>
/// Marca d'água da leitura incremental: até onde a integração já leu do ERP.
/// </summary>
public sealed class SyncWatermark
{
    public required string Name { get; set; }
    public DateTime LastUpdatedAtUtc { get; set; }
    public DateTime LastRunAtUtc { get; set; }
}

public enum IntegrationStatus
{
    Success = 1,
    Error = 2,
    GaveUp = 3
}

/// <summary>
/// Uma linha por item integrado: guarda o último resultado, as tentativas
/// e a mensagem de erro. É a fonte do reprocessamento.
/// </summary>
public sealed class IntegrationLog
{
    public long Id { get; set; }
    public required string Entity { get; set; }
    public required string ExternalId { get; set; }
    public IntegrationStatus Status { get; set; }
    public int Attempts { get; set; }
    public string? Message { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>
/// De-para de status: traduz o status do ERP para o status da loja.
/// Fica em tabela para ser ajustado sem novo deploy.
/// </summary>
public sealed class StatusMapping
{
    public required string ErpStatus { get; set; }
    public required string StoreStatus { get; set; }
    public bool Active { get; set; } = true;
}
