using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SyncWorker.Data;
using SyncWorker.Erp;
using SyncWorker.Sync;

namespace SyncWorker.Tests;

/// <summary>
/// Monta os serviços reais sobre um SQLite em memória e um ERP falso.
/// </summary>
public sealed class TestHarness : IDisposable
{
    private readonly SqliteConnection _connection;

    public TestHarness(SyncOptions? options = null)
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var dbOptions = new DbContextOptionsBuilder<SyncDbContext>()
            .UseSqlite(_connection)
            .Options;

        Db = new SyncDbContext(dbOptions);
        Db.Database.EnsureCreated();
        DbSeeder.SeedStatusMappingsAsync(Db).GetAwaiter().GetResult();

        Options = Microsoft.Extensions.Options.Options.Create(options ?? new SyncOptions { PageSize = 2, OverlapMinutes = 5 });
        Upserter = new OrderUpserter(Db, TimeProvider.System, NullLogger<OrderUpserter>.Instance);
        Sync = new OrderSyncService(Db, Erp, Upserter, Options, TimeProvider.System, NullLogger<OrderSyncService>.Instance);
        Reprocess = new ReprocessService(Db, Erp, Upserter, Options, NullLogger<ReprocessService>.Instance);
    }

    public SyncDbContext Db { get; }
    public FakeErpClient Erp { get; } = new();
    public IOptions<SyncOptions> Options { get; }
    public OrderUpserter Upserter { get; }
    public OrderSyncService Sync { get; }
    public ReprocessService Reprocess { get; }

    public IntegrationLog LogOf(string erpOrderId) =>
        Db.IntegrationLogs.AsNoTracking().Single(l => l.ExternalId == erpOrderId);

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}

/// <summary>ERP em memória com a mesma semântica de paginação do ERP real.</summary>
public sealed class FakeErpClient : IErpClient
{
    private readonly Dictionary<string, ErpOrderDto> _orders = new();

    public int PageRequests { get; private set; }

    public void Upsert(ErpOrderDto order) => _orders[order.Id] = order;

    public void Remove(string id) => _orders.Remove(id);

    public Task<PagedResult<ErpOrderDto>> GetOrdersAsync(DateTime modifiedSinceUtc, int page, int pageSize, CancellationToken ct)
    {
        PageRequests++;

        var filtered = _orders.Values
            .Where(o => o.UpdatedAtUtc >= modifiedSinceUtc)
            .OrderBy(o => o.UpdatedAtUtc)
            .ThenBy(o => o.Id, StringComparer.Ordinal)
            .ToList();

        var items = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Task.FromResult(new PagedResult<ErpOrderDto>(items, page, pageSize, page * pageSize < filtered.Count));
    }

    public Task<ErpOrderDto?> GetOrderAsync(string id, CancellationToken ct) =>
        Task.FromResult(_orders.GetValueOrDefault(id));
}

public static class Orders
{
    public static readonly DateTime BaseTime = new(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

    public static ErpOrderDto Make(string id, int minutes, string status = "ABERTO", decimal total = 100m) =>
        new(id, "Cliente Teste", total, status, BaseTime.AddMinutes(minutes));
}
