using Microsoft.Extensions.Options;

namespace SyncWorker.Sync;

/// <summary>
/// Executa um ciclo (leitura incremental + reprocesso) a cada intervalo.
/// Uma falha no ciclo é registrada e o próximo ciclo tenta de novo.
/// </summary>
public sealed class SyncBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<SyncOptions> options,
    ILogger<SyncBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.IntervalSeconds));

        do
        {
            try
            {
                await RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ciclo de sincronização falhou; nova tentativa no próximo ciclo");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();

        var sync = scope.ServiceProvider.GetRequiredService<OrderSyncService>();
        await sync.RunAsync(ct);

        var reprocess = scope.ServiceProvider.GetRequiredService<ReprocessService>();
        await reprocess.RunAsync(ct);
    }
}
