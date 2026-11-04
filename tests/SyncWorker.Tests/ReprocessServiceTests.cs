using Microsoft.EntityFrameworkCore;
using SyncWorker.Data;

namespace SyncWorker.Tests;

public sealed class ReprocessServiceTests
{
    [Fact]
    public async Task Error_is_recovered_after_the_missing_mapping_is_registered()
    {
        using var h = new TestHarness();
        h.Erp.Upsert(Orders.Make("PED-1", 1, status: "EM_ANALISE"));
        await h.Sync.RunAsync(CancellationToken.None);

        h.Db.StatusMappings.Add(new StatusMapping { ErpStatus = "EM_ANALISE", StoreStatus = "UnderReview" });
        await h.Db.SaveChangesAsync();

        var result = await h.Reprocess.RunAsync(CancellationToken.None);

        Assert.Equal(1, result.Recovered);
        Assert.Equal(IntegrationStatus.Success, h.LogOf("PED-1").Status);
        Assert.Equal("UnderReview", (await h.Db.Orders.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task Item_gives_up_after_the_maximum_number_of_attempts()
    {
        using var h = new TestHarness(new SyncOptions { PageSize = 10, MaxReprocessAttempts = 3 });
        h.Erp.Upsert(Orders.Make("PED-1", 1, status: "EM_ANALISE"));
        await h.Sync.RunAsync(CancellationToken.None); // tentativa 1

        await h.Reprocess.RunAsync(CancellationToken.None); // tentativa 2
        var last = await h.Reprocess.RunAsync(CancellationToken.None); // tentativa 3

        Assert.Equal(1, last.GaveUp);
        Assert.Equal(IntegrationStatus.GaveUp, h.LogOf("PED-1").Status);

        var afterGivingUp = await h.Reprocess.RunAsync(CancellationToken.None);
        Assert.Equal(0, afterGivingUp.Attempted);
    }

    [Fact]
    public async Task Order_removed_from_the_erp_stays_as_error_with_a_clear_message()
    {
        using var h = new TestHarness();
        h.Erp.Upsert(Orders.Make("PED-1", 1, status: "EM_ANALISE"));
        await h.Sync.RunAsync(CancellationToken.None);

        h.Erp.Remove("PED-1");
        var result = await h.Reprocess.RunAsync(CancellationToken.None);

        Assert.Equal(1, result.StillFailing);
        Assert.Equal("Pedido não encontrado no ERP.", h.LogOf("PED-1").Message);
    }

    [Fact]
    public async Task Nothing_to_do_when_there_are_no_errors()
    {
        using var h = new TestHarness();
        h.Erp.Upsert(Orders.Make("PED-1", 1));
        await h.Sync.RunAsync(CancellationToken.None);

        var result = await h.Reprocess.RunAsync(CancellationToken.None);

        Assert.Equal(0, result.Attempted);
    }
}
