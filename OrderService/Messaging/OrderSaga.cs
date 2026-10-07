using OnlineStore.Shared;
using OrderService.Clients;
using OrderService.Data;
using OrderService.Models;

namespace OrderService.Messaging;

public class OrderSaga(OrderDbContext db, IPaymentGateway payments, RabbitMqPublisher bus, ILogger<OrderSaga> log)
{
    // Step 2: inventory reserved -> charge payment.
    public async Task OnStockReservedAsync(StockReserved e, CancellationToken ct)
    {
        var order = await db.Orders.FindAsync([e.OrderId], ct);
        // Idempotency guard: duplicates/late messages are ignored; StockReserved lets a redelivery resume.
        if (order is null || order.Status is not (OrderStatus.Pending or OrderStatus.StockReserved))
        {
            log.LogInformation("Ignoring StockReserved for order {OrderId} (status {Status})", e.OrderId, order?.Status);
            return;
        }

        order.Status = OrderStatus.StockReserved;
        await db.SaveChangesAsync(ct);                       // local tx #1

        var result = await payments.ChargeAsync(order.Id, order.Total, order.ForcePaymentFailure, ct);
        if (result.Success)
        {
            order.Status = OrderStatus.Confirmed;
            order.PaymentId = result.PaymentId;
            log.LogInformation("Order {OrderId} confirmed", order.Id);
        }
        else
        {
            // COMPENSATION: release stock, cancel order
            bus.Publish(RoutingKeys.StockReleaseRequested,
                new StockReleaseRequested(Guid.NewGuid(), Correlation.Current ?? "", order.Id, result.Reason!),
                Correlation.Current);
            order.Status = OrderStatus.Cancelled;
            order.CancelReason = result.Reason;
            log.LogWarning("Order {OrderId} cancelled: {Reason}. Stock release requested.", order.Id, result.Reason);
        }
        await db.SaveChangesAsync(ct);                       // local tx #2
    }

    public async Task OnStockReservationFailedAsync(StockReservationFailed e, CancellationToken ct)
    {
        var order = await db.Orders.FindAsync([e.OrderId], ct);
        if (order is null || order.Status != OrderStatus.Pending) return;
        order.Status = OrderStatus.Cancelled;
        order.CancelReason = $"Stock: {e.Reason}";
        await db.SaveChangesAsync(ct);
        log.LogWarning("Order {OrderId} cancelled: {Reason}", order.Id, e.Reason);
    }

    // On-demand compensation for an already Confirmed order: refund + release stock + cancel.
    public async Task CancelConfirmedAsync(Order order, CancellationToken ct)
    {
        if (order.PaymentId is int paymentId) await payments.RefundAsync(paymentId, ct);
        bus.Publish(RoutingKeys.StockReleaseRequested,
            new StockReleaseRequested(Guid.NewGuid(), Correlation.Current ?? "", order.Id, "Cancelled by customer"),
            Correlation.Current);
        order.Status = OrderStatus.Cancelled;
        order.CancelReason = "Cancelled by customer (refunded)";
        await db.SaveChangesAsync(ct);
    }
}
