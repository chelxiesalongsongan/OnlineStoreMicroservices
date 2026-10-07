using System.Text.Json;

namespace OnlineStore.Shared;

public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

public static class RoutingKeys
{
    public const string OrderPlaced = "order.placed";
    public const string StockReserved = "inventory.reserved";
    public const string StockReservationFailed = "inventory.failed";
    public const string StockReleaseRequested = "inventory.release";   // compensation
}

public record OrderedItem(int ProductId, int Quantity, decimal UnitPrice);

public record OrderPlaced(Guid EventId, string CorrelationId, int OrderId, List<OrderedItem> Items, decimal Total, DateTime OccurredAt);
public record StockReserved(Guid EventId, string CorrelationId, int OrderId);
public record StockReservationFailed(Guid EventId, string CorrelationId, int OrderId, string Reason);
public record StockReleaseRequested(Guid EventId, string CorrelationId, int OrderId, string Reason);
