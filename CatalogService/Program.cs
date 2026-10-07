using CatalogService.Data;
using Microsoft.EntityFrameworkCore;
using OnlineStore.Shared;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddSimpleConsole(o => { o.IncludeScopes = true; o.SingleLine = true; });

builder.Services.AddControllers();
builder.Services.AddProblemDetails();

builder.Services.AddDbContext<CatalogDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("CatalogDb")));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// create/seed the database on a clean checkout
using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database.Migrate();

app.UseMiddleware<CorrelationIdMiddleware>();   // logs and propagates X-Correlation-ID
app.UseExceptionHandler();                      // Problem Details for unhandled errors
app.UseStatusCodePages();                       // Problem Details for plain 404s etc.

app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();

app.Run();
