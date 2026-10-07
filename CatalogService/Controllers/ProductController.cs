using CatalogService.Models;
using Microsoft.AspNetCore.Mvc;

namespace CatalogService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProductController : ControllerBase
    {
        private static readonly List<Product> products = new()
        {
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
                Name = "Leather Seat Cover",
                Price = 8000,
                Stock = 5
            }
        };

        [HttpGet]
        public IActionResult GetProducts()
        {
            return Ok(products);
        }

        [HttpGet("{id}")]
        public IActionResult GetProduct(int id)
        {
            var product = products.FirstOrDefault(p => p.Id == id);

            if (product == null)
            {
                return NotFound();
            }

            return Ok(product);
        }

        [HttpPost]
        public IActionResult CreateProduct(Product product)
        {
            product.Id = products.Count + 1;
            products.Add(product);

            return CreatedAtAction(
                nameof(GetProduct),
                new { id = product.Id },
                product);
        }
    }
}