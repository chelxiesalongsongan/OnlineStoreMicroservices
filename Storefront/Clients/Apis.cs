using Microsoft.AspNetCore.Mvc;

namespace Storefront.Clients;

public record ProductView(int Id, string Name, decimal Price, int Stock);

public class OrderItemView { public int ProductId { get; set; } public int Quantity { get; set; } public decimal UnitPrice { get; set; } }

public class OrderView
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = "";
    public string Status { get; set; } = "";
    public decimal Total { get; set; }
    public string? CancelReason { get; set; }
    public string? CorrelationId { get; set; }
    public List<OrderItemView> Items { get; set; } = new();
}

public class LineModel { public int ProductId { get; set; } public int Quantity { get; set; } }

public class PlaceOrderModel
{
    public string CustomerName { get; set; } = "";
    public List<LineModel> Lines { get; set; } = new();
    public bool ForcePaymentFailure { get; set; }
}

public class CatalogApi(HttpClient http)
{
    public async Task<List<ProductView>> GetProductsAsync(CancellationToken ct) =>
        await http.GetFromJsonAsync<List<ProductView>>("catalog/v1/products", ct) ?? new();
}

public class OrdersApi(HttpClient http)
{
    public async Task<(OrderView? Order, string? Error)> PlaceAsync(PlaceOrderModel m, CancellationToken ct)
    {
        using var resp = await http.PostAsJsonAsync("orders/v1/orders", m, ct);
        if (resp.IsSuccessStatusCode) return (await resp.Content.ReadFromJsonAsync<OrderView>(ct), null);
        return (null, await ReadProblem(resp, ct));
    }

    public async Task<OrderView?> GetAsync(int id, CancellationToken ct) =>
        await http.GetFromJsonAsync<OrderView>($"orders/v1/orders/{id}", ct);

    public async Task<string?> CancelAsync(int id, CancellationToken ct)
    {
        using var resp = await http.PostAsync($"orders/v1/orders/{id}/cancel", null, ct);
        return resp.IsSuccessStatusCode ? null : await ReadProblem(resp, ct);
    }

    private static async Task<string> ReadProblem(HttpResponseMessage resp, CancellationToken ct)
    {
        try
        {
            var p = await resp.Content.ReadFromJsonAsync<ProblemDetails>(ct);
            return p?.Detail ?? p?.Title ?? $"Request failed ({(int)resp.StatusCode})";
        }
        catch { return $"Request failed ({(int)resp.StatusCode})"; }
    }
}
