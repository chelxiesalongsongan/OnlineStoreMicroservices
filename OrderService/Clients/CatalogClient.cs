using System.Net;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace OrderService.Clients;

public record ProductDto(int Id, string Name, decimal Price, int Stock);
public enum CatalogLookup { Found, NotFound, Unavailable }
public record CatalogResult(CatalogLookup Outcome, ProductDto? Product);

public class CatalogClient(HttpClient http, ILogger<CatalogClient> log)
{
    public async Task<CatalogResult> GetProductAsync(int id, CancellationToken ct)
    {
        try
        {
            using var resp = await http.GetAsync($"catalog/v1/products/{id}", ct);   // retries/timeouts happen in the handler
            if (resp.StatusCode == HttpStatusCode.NotFound) return new(CatalogLookup.NotFound, null);
            if (!resp.IsSuccessStatusCode)
            {
                log.LogWarning("Catalog returned {Status} for product {ProductId}", (int)resp.StatusCode, id);
                return new(CatalogLookup.Unavailable, null);
            }
            var product = await resp.Content.ReadFromJsonAsync<ProductDto>(ct);
            return new(CatalogLookup.Found, product);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested &&
            ex is HttpRequestException or TaskCanceledException or TimeoutRejectedException or BrokenCircuitException)
        {
            log.LogError(ex, "Catalog unreachable for product {ProductId}", id);
            return new(CatalogLookup.Unavailable, null);
        }
    }
}
