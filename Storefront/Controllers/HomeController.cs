using Microsoft.AspNetCore.Mvc;
using Storefront.Clients;

namespace Storefront.Controllers;

public class HomeController(CatalogApi catalog) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        try { return View(await catalog.GetProductsAsync(ct)); }
        catch (HttpRequestException)
        {
            ViewBag.Error = "Catalog is currently unavailable.";
            return View(new List<ProductView>());
        }
    }

    public IActionResult Error() => Content("Something went wrong.");
}
