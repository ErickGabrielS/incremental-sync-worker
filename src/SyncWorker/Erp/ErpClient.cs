using System.Globalization;
using System.Net;
using System.Net.Http.Json;

namespace SyncWorker.Erp;

public sealed record ErpOrderDto(string Id, string Customer, decimal Total, string Status, DateTime UpdatedAtUtc);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, bool HasMore);

public interface IErpClient
{
    /// <summary>Pedidos alterados a partir de <paramref name="modifiedSinceUtc"/>, ordenados por data de alteração.</summary>
    Task<PagedResult<ErpOrderDto>> GetOrdersAsync(DateTime modifiedSinceUtc, int page, int pageSize, CancellationToken ct);

    /// <summary>Um pedido pelo id, ou null se não existir mais no ERP.</summary>
    Task<ErpOrderDto?> GetOrderAsync(string id, CancellationToken ct);
}

/// <summary>
/// Cliente HTTP do ERP. Retry, timeout e circuit breaker vêm do
/// AddStandardResilienceHandler configurado no Program.cs.
/// </summary>
public sealed class ErpClient(HttpClient http) : IErpClient
{
    public async Task<PagedResult<ErpOrderDto>> GetOrdersAsync(DateTime modifiedSinceUtc, int page, int pageSize, CancellationToken ct)
    {
        var since = Uri.EscapeDataString(
            DateTime.SpecifyKind(modifiedSinceUtc, DateTimeKind.Utc).ToString("o", CultureInfo.InvariantCulture));

        var url = $"api/orders?modifiedSince={since}&page={page}&pageSize={pageSize}";

        return await http.GetFromJsonAsync<PagedResult<ErpOrderDto>>(url, ct)
               ?? throw new InvalidOperationException("O ERP retornou uma resposta vazia.");
    }

    public async Task<ErpOrderDto?> GetOrderAsync(string id, CancellationToken ct)
    {
        using var response = await http.GetAsync($"api/orders/{Uri.EscapeDataString(id)}", ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ErpOrderDto>(ct);
    }
}
