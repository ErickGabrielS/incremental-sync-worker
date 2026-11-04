namespace FakeErp.Api;

public sealed record ErpOrder(string Id, string Customer, decimal Total, string Status, DateTime UpdatedAtUtc);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, bool HasMore);

/// <summary>Pedidos em memória, protegidos por lock.</summary>
public sealed class OrderStore
{
    private static readonly string[] Customers =
    [
        "Ana Souza", "Bruno Lima", "Carla Mendes", "Diego Rocha", "Elisa Prado",
        "Felipe Costa", "Gabriela Nunes", "Henrique Alves", "Isabela Martins", "João Pereira"
    ];

    internal static readonly string[] Statuses = ["ABERTO", "FATURADO", "ENVIADO", "CANCELADO"];

    private readonly object _gate = new();
    private readonly Dictionary<string, ErpOrder> _orders = new();

    public OrderStore()
    {
        var start = DateTime.UtcNow.AddDays(-3);

        for (var i = 1; i <= 300; i++)
        {
            var id = $"PED-{i:D5}";
            _orders[id] = new ErpOrder(
                id,
                Customers[Random.Shared.Next(Customers.Length)],
                Math.Round((decimal)(Random.Shared.NextDouble() * 900 + 30), 2),
                Statuses[Random.Shared.Next(Statuses.Length)],
                start.AddMinutes(i * 10));
        }
    }

    public PagedResult<ErpOrder> Query(DateTime modifiedSinceUtc, int page, int pageSize)
    {
        lock (_gate)
        {
            var filtered = _orders.Values
                .Where(o => o.UpdatedAtUtc >= modifiedSinceUtc)
                .OrderBy(o => o.UpdatedAtUtc)
                .ThenBy(o => o.Id, StringComparer.Ordinal)
                .ToList();

            var items = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return new PagedResult<ErpOrder>(items, page, pageSize, page * pageSize < filtered.Count);
        }
    }

    public ErpOrder? Find(string id)
    {
        lock (_gate)
        {
            return _orders.GetValueOrDefault(id);
        }
    }

    /// <summary>Altera o status de alguns pedidos, simulando o uso do ERP.</summary>
    public void MutateRandom(int count)
    {
        lock (_gate)
        {
            var ids = _orders.Keys.ToArray();

            for (var i = 0; i < count; i++)
            {
                var id = ids[Random.Shared.Next(ids.Length)];

                // ~10% das alterações usam um status sem de-para no worker.
                var status = Random.Shared.NextDouble() < 0.1
                    ? "EM_ANALISE"
                    : Statuses[Random.Shared.Next(Statuses.Length)];

                _orders[id] = _orders[id] with { Status = status, UpdatedAtUtc = DateTime.UtcNow };
            }
        }
    }
}

public sealed class OrderMutator(OrderStore store, ILogger<OrderMutator> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            store.MutateRandom(3);
            logger.LogInformation("3 pedidos alterados no ERP");
        }
    }
}
