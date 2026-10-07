using System.ComponentModel.DataAnnotations;

namespace OrderService.Models;

public enum OrderStatus { Pending, StockReserved, Confirmed, Cancelled }

public class Order
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = "";
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public decimal Total { get; set; }
    public int? PaymentId { get; set; }
    public string? CancelReason { get; set; }
    public string? CorrelationId { get; set; }
    public bool ForcePaymentFailure { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<OrderItem> Items { get; set; } = new();
}

public class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

public class CreateOrderRequest
{
    [Required, StringLength(100)] public string CustomerName { get; set; } = "";
    [Required, MinLength(1)] public List<OrderLineRequest> Lines { get; set; } = new();
    public bool ForcePaymentFailure { get; set; }
}

public class OrderLineRequest
{
    [Range(1, int.MaxValue)] public int ProductId { get; set; }
    [Range(1, 1000)] public int Quantity { get; set; }
}

public class UpdateOrderRequest
{
    [Required, StringLength(100)] public string CustomerName { get; set; } = "";
}
