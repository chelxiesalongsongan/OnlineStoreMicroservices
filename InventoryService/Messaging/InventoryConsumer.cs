using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OnlineStore.Shared;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace InventoryService.Messaging;

public class InventoryConsumer(IServiceScopeFactory scopes, IOptions<RabbitMqOptions> options, ILogger<InventoryConsumer> log)
    : BackgroundService
{
    private const string PlacedQueue = "inventory.order-placed";
    private const string ReleaseQueue = "inventory.release";

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var conn = options.Value.ToFactory(asyncConsumers: true).CreateConnection("inventory-consumer");
                using var ch = conn.CreateModel();
                RabbitTopology.DeclareConsumerQueue(ch, PlacedQueue, RoutingKeys.OrderPlaced);
                RabbitTopology.DeclareConsumerQueue(ch, ReleaseQueue, RoutingKeys.StockReleaseRequested);
                ch.BasicQos(0, 1, false);   // one message at a time keeps stock updates serialized

                foreach (var queue in new[] { PlacedQueue, ReleaseQueue })
                {
                    var consumer = new AsyncEventingBasicConsumer(ch);
                    consumer.Received += (_, ea) => HandleAsync(ch, ea);
                    ch.BasicConsume(queue, autoAck: false, consumer);   // manual acks
                }
                log.LogInformation("Inventory consumer listening on {Placed} and {Release}", PlacedQueue, ReleaseQueue);

                var closed = new TaskCompletionSource();
                conn.ConnectionShutdown += (_, _) => closed.TrySetResult();
                await Task.WhenAny(closed.Task, Task.Delay(Timeout.Infinite, stop));
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                log.LogError(ex, "RabbitMQ unavailable, retrying in 5s");
                try { await Task.Delay(TimeSpan.FromSeconds(5), stop); } catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task HandleAsync(IModel ch, BasicDeliverEventArgs ea)
    {
        var cid = ea.BasicProperties.Headers is { } h && h.TryGetValue(Correlation.Header, out var raw) && raw is byte[] b
            ? Encoding.UTF8.GetString(b) : Guid.NewGuid().ToString("N");
        Correlation.Current = cid;

        using (log.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = cid }))
        {
            try
            {
                var json = Encoding.UTF8.GetString(ea.Body.Span);
                using var scope = scopes.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<InventoryHandler>();

                switch (ea.RoutingKey)
                {
                    case RoutingKeys.OrderPlaced:
                        await handler.OnOrderPlacedAsync(JsonSerializer.Deserialize<OrderPlaced>(json, Json.Options)!, default);
                        break;
                    case RoutingKeys.StockReleaseRequested:
                        await handler.OnReleaseRequestedAsync(JsonSerializer.Deserialize<StockReleaseRequested>(json, Json.Options)!, default);
                        break;
                    default:
                        log.LogWarning("Unknown routing key {Key}", ea.RoutingKey);
                        break;
                }
                ch.BasicAck(ea.DeliveryTag, false);
            }
            catch (Exception ex)
            {
                // first failure: requeue once; second failure: reject without requeue => dead-letter queue
                log.LogError(ex, "Failed processing {Key} (redelivered={Redelivered})", ea.RoutingKey, ea.Redelivered);
                ch.BasicNack(ea.DeliveryTag, false, requeue: !ea.Redelivered);
            }
        }
    }
}
