using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineStore.Shared;
using OrderService.Clients;
using OrderService.Data;
using OrderService.Messaging;
using OrderService.Models;

namespace OrderService.Controllers;

[ApiController]
[Route("orders/v1/orders")]
public class OrdersController(OrderDbContext db, CatalogClient catalog, RabbitMqPublisher bus,
    OrderSaga saga, ILogger<OrdersController> log) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Order>>> List(CancellationToken ct) =>
        await db.Orders.AsNoTracking().Include(o => o.Items).OrderByDescending(o => o.Id).ToListAsync(ct);

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Order>> GetById(int id, CancellationToken ct)
    {
        var o = await db.Orders.AsNoTracking().Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id, ct);
        return o is null ? NotFoundProblem(id) : o;
    }

    [HttpPost]
    public async Task<ActionResult<Order>> Create(CreateOrderRequest req, CancellationToken ct)
    {
        // 1) synchronous validation against Catalog (retry/timeout in handler)
        var items = new List<OrderItem>();
        foreach (var line in req.Lines)
        {
            var r = await catalog.GetProductAsync(line.ProductId, ct);
            switch (r.Outcome)
            {
                case CatalogLookup.NotFound:
                    return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
                    {
                        ["lines"] = [$"Product {line.ProductId} does not exist."]
                    }));
                case CatalogLookup.Unavailable:
                    return Problem(statusCode: 503, title: "Catalog service unavailable",
                        detail: "Could not validate products right now. Please retry.");
            }
            items.Add(new OrderItem { ProductId = r.Product!.Id, Quantity = line.Quantity, UnitPrice = r.Product.Price });
        }

        // 2) local transaction: save Pending order
        var order = new Order
        {
            CustomerName = req.CustomerName,
            ForcePaymentFailure = req.ForcePaymentFailure,
            CorrelationId = Correlation.Current,
            Items = items,
            Total = items.Sum(i => i.UnitPrice * i.Quantity)
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);

        // 3) publish OrderPlaced (publisher confirms)
        try
        {
            bus.Publish(RoutingKeys.OrderPlaced,
                new OrderPlaced(Guid.NewGuid(), Correlation.Current ?? "", order.Id,
                    items.Select(i => new OrderedItem(i.ProductId, i.Quantity, i.UnitPrice)).ToList(),
                    order.Total, DateTime.UtcNow),
                Correlation.Current);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Could not publish OrderPlaced for order {OrderId}", order.Id);
            order.Status = OrderStatus.Cancelled;
            order.CancelReason = "Messaging unavailable";
            await db.SaveChangesAsync(ct);
            return Problem(statusCode: 503, title: "Order could not be processed", detail: "Message broker unavailable.");
        }

        return CreatedAtAction(nameof(GetById), new { id = order.Id }, order);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<Order>> Update(int id, UpdateOrderRequest req, CancellationToken ct)
    {
        var o = await db.Orders.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (o is null) return NotFoundProblem(id);
        o.CustomerName = req.CustomerName;
        await db.SaveChangesAsync(ct);
        return o;
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var o = await db.Orders.FindAsync([id], ct);
        if (o is null) return NotFoundProblem(id);
        db.Orders.Remove(o);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // On-demand compensation demo
    [HttpPost("{id:int}/cancel")]
    public async Task<ActionResult<Order>> Cancel(int id, CancellationToken ct)
    {
        var o = await db.Orders.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (o is null) return NotFoundProblem(id);
        if (o.Status == OrderStatus.Cancelled) return o;
        if (o.Status != OrderStatus.Confirmed)
            return Problem(statusCode: 409, title: "Only confirmed orders can be cancelled manually");
        await saga.CancelConfirmedAsync(o, ct);
        return o;
    }

    private ObjectResult NotFoundProblem(int id) =>
        Problem(statusCode: 404, title: "Order not found", detail: $"No order with id {id}.");
}
