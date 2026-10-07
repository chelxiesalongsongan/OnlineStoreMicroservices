using Microsoft.AspNetCore.Mvc;
using Storefront.Clients;

namespace Storefront.Controllers;

public class OrdersController(OrdersApi orders) : Controller
{
    [HttpPost]
    public async Task<IActionResult> Create(PlaceOrderModel model, CancellationToken ct)
    {
        model.Lines = model.Lines.Where(l => l.Quantity > 0).ToList();
        if (model.Lines.Count == 0 || string.IsNullOrWhiteSpace(model.CustomerName))
        {
            TempData["Error"] = "Enter your name and at least one quantity.";
            return RedirectToAction("Index", "Home");
        }
        var (order, error) = await orders.PlaceAsync(model, ct);
        if (order is null)
        {
            TempData["Error"] = error;
            return RedirectToAction("Index", "Home");
        }
        return RedirectToAction(nameof(Details), new { id = order.Id });
    }

    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var order = await orders.GetAsync(id, ct);
        return order is null ? NotFound() : View(order);
    }

    [HttpPost]
    public async Task<IActionResult> Cancel(int id, CancellationToken ct)
    {
        TempData["Error"] = await orders.CancelAsync(id, ct);
        return RedirectToAction(nameof(Details), new { id });
    }
}
