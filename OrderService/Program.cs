using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
using OnlineStore.Shared;
using OrderService.Clients;
using OrderService.Clients.Generated;
using OrderService.Data;
using OrderService.Messaging;
using Polly;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddSimpleConsole(o => { o.IncludeScopes = true; o.SingleLine = true; });

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<OrderDbContext>(o => o.UseSqlite(builder.Configuration.GetConnectionString("OrderDb")));
builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection("RabbitMq"));
builder.Services.AddSingleton<RabbitMqPublisher>();

builder.Services.AddTransient<CorrelationIdHandler>();

builder.Services.AddHttpClient<CatalogClient>(c => c.BaseAddress = new Uri(builder.Configuration["Services:CatalogBaseUrl"]!))
    .AddHttpMessageHandler<CorrelationIdHandler>()
    .AddStandardResilienceHandler(ConfigureResilience);

builder.Services.AddHttpClient<IPaymentClient, PaymentClient>(c => c.BaseAddress = new Uri(builder.Configuration["Services:PaymentBaseUrl"]!))
    .AddHttpMessageHandler<CorrelationIdHandler>()
    .AddStandardResilienceHandler(ConfigureResilience);   // charge is idempotent per orderId, so retrying POST is safe

builder.Services.AddScoped<IPaymentGateway, PaymentGateway>();
builder.Services.AddScoped<OrderSaga>();
builder.Services.AddHostedService<OrderSagaConsumer>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<OrderDbContext>().Database.Migrate();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();
app.Run();

static void ConfigureResilience(HttpStandardResilienceOptions o)
{
    o.Retry.MaxRetryAttempts = 3;
    o.Retry.BackoffType = DelayBackoffType.Exponential;
    o.Retry.UseJitter = true;
    o.Retry.Delay = TimeSpan.FromMilliseconds(300);
    o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(3);          // per attempt
    o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(15);    // overall
    o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);// must be >= 2x attempt timeout
}
