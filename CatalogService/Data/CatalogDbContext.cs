using CatalogService.Models;
using Microsoft.EntityFrameworkCore;

namespace CatalogService.Data
{
    public class CatalogDbContext : DbContext
    {
        public CatalogDbContext(DbContextOptions<CatalogDbContext> options)
            : base(options)
        {
        }

        public DbSet<Product> Products { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Product>().HasData(
                new Product
                {
                    Id = 1,
                    Name = "Car Window Tint",
                    Price = 5000,
                    Stock = 10
                },
                new Product
                {
                    Id = 2,
                    Name = "Premium Car Window Tint",
                    Price = 7500,
                    Stock = 8
                },
                new Product
                {
                    Id = 3,
                    Name = "Leather Seat Cover",
                    Price = 12000,
                    Stock = 5
                }
            );
        }
    }
}