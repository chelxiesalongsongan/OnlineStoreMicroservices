using Microsoft.Extensions.Http.Resilience;
using OnlineStore.Shared;
using Storefront.Clients;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddSimpleConsole(o => { o.IncludeScopes = true; o.SingleLine = true; });
builder.Services.AddControllersWithViews();
builder.Services.AddTransient<CorrelationIdHandler>();

builder.Services.AddHttpClient<CatalogApi>(c => c.BaseAddress = new Uri(builder.Configuration["Services:CatalogBaseUrl"]!))
    .AddHttpMessageHandler<CorrelationIdHandler>()
    .AddStandardResilienceHandler();

builder.Services.AddHttpClient<OrdersApi>(c => c.BaseAddress = new Uri(builder.Configuration["Services:OrderBaseUrl"]!))
    .AddHttpMessageHandler<CorrelationIdHandler>()
    .AddStandardResilienceHandler(o =>
    {
        // retry only idempotent GETs; never retry POST (no duplicate orders)
        o.Retry.ShouldHandle = args =>
        {
            var r = args.Outcome.Result;
            var retry = r is not null && r.RequestMessage?.Method == HttpMethod.Get &&
                        ((int)r.StatusCode >= 500 || (int)r.StatusCode == 408);
            return ValueTask.FromResult(retry);
        };
    });

var app = builder.Build();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler("/Home/Error");
app.UseStaticFiles();
app.UseRouting();
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
app.Run();

