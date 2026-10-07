using Microsoft.EntityFrameworkCore;
using InventoryService.Data;
using InventoryService.Models;
using OnlineStore.Shared;

namespace InventoryService.Messaging;

public class InventoryHandler(InventoryDbContext db, RabbitMqPublisher bus, ILogger<InventoryHandler> log)
{
    // Reserve stock for an order. Local transaction: stock counters + reservation + idempotency marker.
    public async Task OnOrderPlacedAsync(OrderPlaced e, CancellationToken ct)
    {
        if (await db.ProcessedMessages.AnyAsync(m => m.EventId == e.EventId, ct))
        {
            log.LogInformation("Duplicate OrderPlaced {EventId} for order {OrderId}; replaying reply", e.EventId, e.OrderId);
            await ReplayReplyAsync(e, ct);
            return;
        }

        // one reservation per order, even if a different event id arrives for the same order
        if (await db.Reservations.AnyAsync(r => r.OrderId == e.OrderId, ct))
        {
            db.ProcessedMessages.Add(new ProcessedMessage { EventId = e.EventId, Type = nameof(OrderPlaced) });
            await db.SaveChangesAsync(ct);
            await ReplayReplyAsync(e, ct);
            return;
        }

        var wanted = e.Items.GroupBy(i => i.ProductId).ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));
        var ids = wanted.Keys.ToList();
        var stock = await db.StockItems.Where(s => ids.Contains(s.ProductId)).ToListAsync(ct);

        string? failure = null;
        foreach (var (productId, qty) in wanted)
        {
            var s = stock.FirstOrDefault(x => x.ProductId == productId);
            if (s is null) { failure = $"Product {productId} has no stock record"; break; }
            if (s.Available < qty)
            {
                failure = $"Insufficient stock for product {productId} (available {s.Available}, requested {qty})";
                break;
            }
        }

        db.ProcessedMessages.Add(new ProcessedMessage { EventId = e.EventId, Type = nameof(OrderPlaced) });
        if (failure is null)
        {
            var reservation = new Reservation { OrderId = e.OrderId };
            foreach (var (productId, qty) in wanted)
            {
                stock.First(x => x.ProductId == productId).Reserved += qty;
                reservation.Lines.Add(new ReservationLine { ProductId = productId, Quantity = qty });
            }
            db.Reservations.Add(reservation);
        }
        await db.SaveChangesAsync(ct);      // atomic: reservation + stock counters + idempotency marker

        var cid = Correlation.Current ?? e.CorrelationId;
        if (failure is null)
        {
            log.LogInformation("Reserved stock for order {OrderId}", e.OrderId);
            bus.Publish(RoutingKeys.StockReserved, new StockReserved(Guid.NewGuid(), cid, e.OrderId), cid);
        }
        else
        {
            log.LogWarning("Could not reserve stock for order {OrderId}: {Reason}", e.OrderId, failure);
            bus.Publish(RoutingKeys.StockReservationFailed,
                new StockReservationFailed(Guid.NewGuid(), cid, e.OrderId, failure), cid);
        }
    }

    // Compensation: release the units reserved for an order. Safe to repeat.
    public async Task OnReleaseRequestedAsync(StockReleaseRequested e, CancellationToken ct)
    {
        if (await db.ProcessedMessages.AnyAsync(m => m.EventId == e.EventId, ct))
        {
            log.LogInformation("Duplicate StockReleaseRequested {EventId}; ignored", e.EventId);
            return;
        }

        var reservation = await db.Reservations.Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.OrderId == e.OrderId, ct);

        if (reservation is { Status: ReservationStatus.Reserved })
        {
            var ids = reservation.Lines.Select(l => l.ProductId).ToList();
            var stock = await db.StockItems.Where(s => ids.Contains(s.ProductId)).ToListAsync(ct);
            foreach (var line in reservation.Lines)
            {
                var s = stock.FirstOrDefault(x => x.ProductId == line.ProductId);
                if (s is not null) s.Reserved = Math.Max(0, s.Reserved - line.Quantity);
            }
            reservation.Status = ReservationStatus.Released;
            log.LogInformation("Released stock for order {OrderId} ({Reason})", e.OrderId, e.Reason);
        }
        else
        {
            log.LogInformation("Nothing to release for order {OrderId}", e.OrderId);
        }

        db.ProcessedMessages.Add(new ProcessedMessage { EventId = e.EventId, Type = nameof(StockReleaseRequested) });
        await db.SaveChangesAsync(ct);
    }

    // Re-send the outcome of an earlier attempt (the first reply may have been lost before the ack).
    private async Task ReplayReplyAsync(OrderPlaced e, CancellationToken ct)
    {
        var cid = Correlation.Current ?? e.CorrelationId;
        if (await db.Reservations.AnyAsync(r => r.OrderId == e.OrderId, ct))
            bus.Publish(RoutingKeys.StockReserved, new StockReserved(Guid.NewGuid(), cid, e.OrderId), cid);
        else
            bus.Publish(RoutingKeys.StockReservationFailed,
                new StockReservationFailed(Guid.NewGuid(), cid, e.OrderId, "Stock was not available"), cid);
    }
}
