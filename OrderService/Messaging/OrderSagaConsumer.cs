using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OnlineStore.Shared;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OrderService.Messaging;

public class OrderSagaConsumer(IServiceScopeFactory scopes, IOptions<RabbitMqOptions> options, ILogger<OrderSagaConsumer> log)
    : BackgroundService
{
    private const string Queue = "order.saga";

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var conn = options.Value.ToFactory(asyncConsumers: true).CreateConnection("order-saga");
                using var ch = conn.CreateModel();
                RabbitTopology.DeclareConsumerQueue(ch, Queue, RoutingKeys.StockReserved, RoutingKeys.StockReservationFailed);
                ch.BasicQos(0, 1, false);

                var consumer = new AsyncEventingBasicConsumer(ch);
                consumer.Received += (_, ea) => HandleAsync(ch, ea);
                ch.BasicConsume(Queue, autoAck: false, consumer);   // manual acks
                log.LogInformation("Saga consumer listening on {Queue}", Queue);

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
                var saga = scope.ServiceProvider.GetRequiredService<OrderSaga>();

                switch (ea.RoutingKey)
                {
                    case RoutingKeys.StockReserved:
                        await saga.OnStockReservedAsync(JsonSerializer.Deserialize<StockReserved>(json, Json.Options)!, default);
                        break;
                    case RoutingKeys.StockReservationFailed:
                        await saga.OnStockReservationFailedAsync(JsonSerializer.Deserialize<StockReservationFailed>(json, Json.Options)!, default);
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
