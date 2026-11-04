using Microsoft.EntityFrameworkCore;
using SyncWorker.Data;
using SyncWorker.Sync;

namespace SyncWorker.Tests;

public sealed class OrderSyncServiceTests
{
    [Fact]
    public async Task First_run_imports_every_page_and_advances_the_watermark()
    {
        using var h = new TestHarness();
        h.Erp.Upsert(Orders.Make("PED-1", 1));
        h.Erp.Upsert(Orders.Make("PED-2", 2));
        h.Erp.Upsert(Orders.Make("PED-3", 3));

        var result = await h.Sync.RunAsync(CancellationToken.None);

        Assert.Equal(3, result.Created);
        Assert.Equal(2, h.Erp.PageRequests); // PageSize = 2 → duas páginas
        Assert.Equal(3, await h.Db.Orders.CountAsync());

        var watermark = await h.Db.SyncWatermarks.AsNoTracking().SingleAsync();
        Assert.Equal(Orders.BaseTime.AddMinutes(3), watermark.LastUpdatedAtUtc);
    }

    [Fact]
    public async Task Running_again_with_the_same_data_skips_and_keeps_the_original_log_date()
    {
        using var h = new TestHarness();
        h.Erp.Upsert(Orders.Make("PED-1", 10));

        await h.Sync.RunAsync(CancellationToken.None);
        var firstLog = h.LogOf("PED-1");

        await Task.Delay(20);
        var second = await h.Sync.RunAsync(CancellationToken.None);

        Assert.Equal(0, second.Created);
        Assert.Equal(1, second.Skipped);
        Assert.Equal(firstLog.UpdatedAtUtc, h.LogOf("PED-1").UpdatedAtUtc);
    }

    [Fact]
    public async Task A_newer_version_in_the_erp_updates_the_store_order()
    {
        using var h = new TestHarness();
        h.Erp.Upsert(Orders.Make("PED-1", 10, status: "ABERTO"));
        await h.Sync.RunAsync(CancellationToken.None);

        h.Erp.Upsert(Orders.Make("PED-1", 20, status: "FATURADO"));
        var result = await h.Sync.RunAsync(CancellationToken.None);

        Assert.Equal(1, result.Updated);
        var order = await h.Db.Orders.AsNoTracking().SingleAsync();
        Assert.Equal("Invoiced", order.Status);
    }

    [Fact]
    public async Task Overlap_window_catches_a_record_saved_late_by_the_erp()
    {
        using var h = new TestHarness();
        h.Erp.Upsert(Orders.Make("PED-1", 60));
        await h.Sync.RunAsync(CancellationToken.None); // marca d'água = minuto 60

        // O ERP grava um pedido com data de alteração anterior à marca d'água
        // (transação que demorou a confirmar). Está dentro da margem de 5 minutos.
        h.Erp.Upsert(Orders.Make("PED-LATE", 57));
        var result = await h.Sync.RunAsync(CancellationToken.None);

        Assert.Equal(1, result.Created);
        Assert.True(await h.Db.Orders.AnyAsync(o => o.ErpOrderId == "PED-LATE"));
    }

    [Fact]
    public async Task Status_without_mapping_is_logged_as_error_and_does_not_block_the_others()
    {
        using var h = new TestHarness();
        h.Erp.Upsert(Orders.Make("PED-1", 1));
        h.Erp.Upsert(Orders.Make("PED-2", 2, status: "EM_ANALISE"));
        h.Erp.Upsert(Orders.Make("PED-3", 3));

        var result = await h.Sync.RunAsync(CancellationToken.None);

        Assert.Equal(2, result.Created);
        Assert.Equal(1, result.Failed);

        var log = h.LogOf("PED-2");
        Assert.Equal(IntegrationStatus.Error, log.Status);
        Assert.Equal(1, log.Attempts);
        Assert.Contains("de-para", log.Message);
    }

    [Fact]
    public async Task Negative_total_is_rejected()
    {
        using var h = new TestHarness();
        h.Erp.Upsert(Orders.Make("PED-1", 1, total: -10m));

        var result = await h.Sync.RunAsync(CancellationToken.None);

        Assert.Equal(1, result.Failed);
        Assert.False(await h.Db.Orders.AnyAsync());
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 0)]
    public void ComputeStart_reads_everything_on_the_first_run(int watermarkMinutesAfterMin, int expectedMinutesAfterMin)
    {
        var watermark = DateTime.MinValue.AddMinutes(watermarkMinutesAfterMin);

        var start = OrderSyncService.ComputeStart(watermark, TimeSpan.FromMinutes(5));

        Assert.Equal(DateTime.MinValue.AddMinutes(expectedMinutesAfterMin), start);
    }

    [Fact]
    public void ComputeStart_subtracts_the_overlap()
    {
        var start = OrderSyncService.ComputeStart(Orders.BaseTime, TimeSpan.FromMinutes(5));

        Assert.Equal(Orders.BaseTime.AddMinutes(-5), start);
    }
}
