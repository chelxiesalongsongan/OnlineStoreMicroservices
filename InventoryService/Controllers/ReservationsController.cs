using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using InventoryService.Data;
using InventoryService.Models;

namespace InventoryService.Controllers;

[ApiController]
[Route("inventory/v1/reservations")]
public class ReservationsController(InventoryDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Reservation>>> List(CancellationToken ct) =>
        await db.Reservations.AsNoTracking().Include(r => r.Lines).OrderByDescending(r => r.Id).ToListAsync(ct);
}
