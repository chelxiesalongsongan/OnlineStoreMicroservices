using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using InventoryService.Data;
using InventoryService.Models;

namespace InventoryService.Controllers;

[ApiController]
[Route("inventory/v1/stock")]
public class StockController(InventoryDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<StockItem>>> List(CancellationToken ct) =>
        await db.StockItems.AsNoTracking().OrderBy(s => s.Id).ToListAsync(ct);

    [HttpGet("{id:int}")]
    public async Task<ActionResult<StockItem>> GetById(int id, CancellationToken ct)
    {
        var item = await db.StockItems.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        return item is null ? NotFoundProblem(id) : item;
    }

    [HttpPost]
    public async Task<ActionResult<StockItem>> Create(StockItemRequest req, CancellationToken ct)
    {
        if (await db.StockItems.AnyAsync(s => s.ProductId == req.ProductId, ct))
            return Problem(statusCode: 409, title: "Stock item already exists",
                detail: $"Product {req.ProductId} already has a stock record.");

        var item = new StockItem { ProductId = req.ProductId, QuantityOnHand = req.QuantityOnHand };
        db.StockItems.Add(item);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(GetById), new { id = item.Id }, item);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<StockItem>> Update(int id, StockItemRequest req, CancellationToken ct)
    {
        var item = await db.StockItems.FindAsync([id], ct);
        if (item is null) return NotFoundProblem(id);

        if (req.ProductId != item.ProductId &&
            await db.StockItems.AnyAsync(s => s.ProductId == req.ProductId, ct))
            return Problem(statusCode: 409, title: "Stock item already exists",
                detail: $"Product {req.ProductId} already has a stock record.");

        if (req.QuantityOnHand < item.Reserved)
            return Problem(statusCode: 409, title: "Quantity below reserved stock",
                detail: $"{item.Reserved} units are reserved by open orders.");

        item.ProductId = req.ProductId;
        item.QuantityOnHand = req.QuantityOnHand;
        await db.SaveChangesAsync(ct);
        return item;
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var item = await db.StockItems.FindAsync([id], ct);
        if (item is null) return NotFoundProblem(id);
        if (item.Reserved > 0)
            return Problem(statusCode: 409, title: "Stock is reserved",
                detail: $"{item.Reserved} units are reserved by open orders.");

        db.StockItems.Remove(item);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private ObjectResult NotFoundProblem(int id) =>
        Problem(statusCode: 404, title: "Stock item not found", detail: $"No stock item with id {id}.");
}
