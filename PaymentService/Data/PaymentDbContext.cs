using Microsoft.EntityFrameworkCore;
using PaymentService.Models;

namespace PaymentService.Data;

public class PaymentDbContext(DbContextOptions<PaymentDbContext> options) : DbContext(options)
{
    public DbSet<Payment> Payments => Set<Payment>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Payment>().HasIndex(p => p.OrderId).IsUnique();   // one payment per order => idempotent charge
        b.Entity<Payment>().Property(p => p.Status).HasConversion<string>();
    }
}
