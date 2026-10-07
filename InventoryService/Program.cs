using Microsoft.EntityFrameworkCore;
using InventoryService.Data;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddDbContext<InventoryDbContext>(o => o.UseSqlite(builder.Configuration.GetConnectionString("InventoryDb")));
var app = builder.Build();
app.MapControllers();
app.Run();
