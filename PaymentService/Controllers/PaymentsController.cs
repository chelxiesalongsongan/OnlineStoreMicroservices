using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineStore.Shared;
using PaymentService.Data;
using PaymentService.Models;

namespace PaymentService.Controllers;

[ApiController]
[Route("payments/v1/payments")]
public class PaymentsController(PaymentDbContext db, ILogger<PaymentsController> log) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Payment>>> List() =>
        await db.Payments.AsNoTracking().OrderBy(p => p.Id).ToListAsync();

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Payment>> GetById(int id)
    {
        var p = await db.Payments.FindAsync(id);
        return p is null ? NotFoundProblem(id) : p;
    }

    [HttpPost]
    public async Task<ActionResult<Payment>> Create(ChargeRequest req)
    {
        if (await db.Payments.AnyAsync(p => p.OrderId == req.OrderId))
            return Problem(statusCode: 409, title: "Payment already exists for this order");
        var p = New(req, PaymentStatus.Pending);
        db.Payments.Add(p);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = p.Id }, p);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<Payment>> Update(int id, UpdatePaymentRequest req)
    {
        var p = await db.Payments.FindAsync(id);
        if (p is null) return NotFoundProblem(id);
        p.Status = req.Status;
        await db.SaveChangesAsync();
        return p;
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var p = await db.Payments.FindAsync(id);
        if (p is null) return NotFoundProblem(id);
        db.Payments.Remove(p);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ---- Saga step (idempotent per OrderId) ----
    [HttpPost("charge")]
    public async Task<ActionResult<Payment>> Charge(ChargeRequest req)
    {
        var p = await db.Payments.FirstOrDefaultAsync(x => x.OrderId == req.OrderId);
        if (p is null)
        {
            p = New(req, req.ForceFailure ? PaymentStatus.Failed : PaymentStatus.Charged);
            db.Payments.Add(p);
            await db.SaveChangesAsync();
            log.LogInformation("Payment {PaymentId} for order {OrderId} -> {Status}", p.Id, p.OrderId, p.Status);
        }
        return p.Status == PaymentStatus.Failed
            ? Problem(statusCode: 402, title: "Payment declined", detail: $"Payment for order {req.OrderId} was declined.")
            : p;
    }

    // ---- Compensation ----
    [HttpPost("{id:int}/refund")]
    public async Task<ActionResult<Payment>> Refund(int id)
    {
        var p = await db.Payments.FindAsync(id);
        if (p is null) return NotFoundProblem(id);
        if (p.Status == PaymentStatus.Charged)
        {
            p.Status = PaymentStatus.Refunded;
            await db.SaveChangesAsync();
            log.LogInformation("Payment {PaymentId} refunded", id);
        }
        return p;   // already refunded => same result (idempotent)
    }

    private static Payment New(ChargeRequest r, PaymentStatus s) => new()
    {
        OrderId = r.OrderId, Amount = r.Amount, Status = s, CorrelationId = Correlation.Current
    };

    private ObjectResult NotFoundProblem(int id) =>
        Problem(statusCode: 404, title: "Payment not found", detail: $"No payment with id {id}.");
}
