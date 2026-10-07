using Microsoft.EntityFrameworkCore;
using InventoryService.Models;

namespace InventoryService.Data;

public class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<StockItem> StockItems => Set<StockItem>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<ReservationLine> ReservationLines => Set<ReservationLine>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<StockItem>().HasIndex(s => s.ProductId).IsUnique();

        b.Entity<Reservation>().HasIndex(r => r.OrderId).IsUnique();   // one reservation per order
        b.Entity<Reservation>().Property(r => r.Status).HasConversion<string>();
        b.Entity<Reservation>().HasMany(r => r.Lines).WithOne().HasForeignKey(l => l.ReservationId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<ProcessedMessage>().HasKey(m => m.EventId);

        // seed data: stock for the Catalog's seeded products 1..3
        b.Entity<StockItem>().HasData(
            new StockItem { Id = 1, ProductId = 1, QuantityOnHand = 10, Reserved = 0 },
            new StockItem { Id = 2, ProductId = 2, QuantityOnHand = 8, Reserved = 0 },
            new StockItem { Id = 3, ProductId = 3, QuantityOnHand = 5, Reserved = 0 });
    }
}
