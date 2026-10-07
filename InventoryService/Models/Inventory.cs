using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventoryService.Models;

public class StockItem
{
    public int Id { get; set; }
    public int ProductId { get; set; }          // Catalog product id (no cross-database link)
    public int QuantityOnHand { get; set; }
    public int Reserved { get; set; }

    [NotMapped]
    public int Available => QuantityOnHand - Reserved;
}

public class StockItemRequest
{
    [Range(1, int.MaxValue)] public int ProductId { get; set; }
    [Range(0, int.MaxValue)] public int QuantityOnHand { get; set; }
}

public enum ReservationStatus { Reserved, Released }

public class Reservation
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public ReservationStatus Status { get; set; } = ReservationStatus.Reserved;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<ReservationLine> Lines { get; set; } = new();
}

public class ReservationLine
{
    public int Id { get; set; }
    public int ReservationId { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }
}

/// One row per handled event; the primary key makes the consumer idempotent.
public class ProcessedMessage
{
    public Guid EventId { get; set; }
    public string Type { get; set; } = "";
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}
