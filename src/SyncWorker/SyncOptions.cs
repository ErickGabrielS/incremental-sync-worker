namespace SyncWorker;

/// <summary>
/// Configurações do ciclo de sincronização (seção "Sync" do appsettings).
/// </summary>
public sealed class SyncOptions
{
    public const string Section = "Sync";

    /// <summary>Intervalo entre ciclos do worker, em segundos.</summary>
    public int IntervalSeconds { get; set; } = 30;

    /// <summary>Quantidade de pedidos por página na leitura do ERP.</summary>
    public int PageSize { get; set; } = 50;

    /// <summary>
    /// Margem de segurança subtraída da marca d'água a cada leitura.
    /// Evita perder registros que o ERP grava com atraso (lacuna de horário).
    /// A idempotência garante que os registros relidos não sejam duplicados.
    /// </summary>
    public int OverlapMinutes { get; set; } = 5;

    /// <summary>Máximo de tentativas de reprocessamento de um item com erro.</summary>
    public int MaxReprocessAttempts { get; set; } = 5;

    /// <summary>Quantos itens com erro são reprocessados por ciclo.</summary>
    public int ReprocessBatchSize { get; set; } = 20;
}
