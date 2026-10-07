using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using InventoryService.Data;
using OnlineStore.Shared;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddSimpleConsole(o => { o.IncludeScopes = true; o.SingleLine = true; });

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<InventoryDbContext>(o => o.UseSqlite(builder.Configuration.GetConnectionString("InventoryDb")));

var app = builder.Build();

// create + seed the database on a clean checkout
using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<InventoryDbContext>().Database.Migrate();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();
app.Run();
