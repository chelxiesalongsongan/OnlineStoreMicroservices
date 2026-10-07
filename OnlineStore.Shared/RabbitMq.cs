using System.Text.Json;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace OnlineStore.Shared;

public class RabbitMqOptions
{
    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string VirtualHost { get; set; } = "/";
    public string UserName { get; set; } = "";   // set via user-secrets
    public string Password { get; set; } = "";   // set via user-secrets

    public ConnectionFactory ToFactory(bool asyncConsumers = false) => new()
    {
        HostName = HostName, Port = Port, VirtualHost = VirtualHost,
        UserName = UserName, Password = Password, DispatchConsumersAsync = asyncConsumers
    };
}

public static class RabbitTopology
{
    public const string Exchange = "store.events";
    public const string DeadLetterExchange = "store.dlx";

    public static void DeclareExchanges(IModel ch)
    {
        ch.ExchangeDeclare(Exchange, ExchangeType.Topic, durable: true);
        ch.ExchangeDeclare(DeadLetterExchange, ExchangeType.Direct, durable: true);
    }

    /// Declares "<queue>" (dead-lettering to "<queue>.dlq") and binds it. Every service must use this helper
    /// so queue arguments match.
    public static void DeclareConsumerQueue(IModel ch, string queue, params string[] routingKeys)
    {
        DeclareExchanges(ch);
        var dlq = $"{queue}.dlq";
        ch.QueueDeclare(dlq, durable: true, exclusive: false, autoDelete: false, arguments: null);
        ch.QueueBind(dlq, DeadLetterExchange, queue);
        ch.QueueDeclare(queue, durable: true, exclusive: false, autoDelete: false, arguments: new Dictionary<string, object>
        {
            ["x-dead-letter-exchange"] = DeadLetterExchange,
            ["x-dead-letter-routing-key"] = queue
        });
        foreach (var key in routingKeys) ch.QueueBind(queue, Exchange, key);
    }
}

/// Publishes with publisher confirms + mandatory flag (throws if broker did not accept or nothing is bound).
public sealed class RabbitMqPublisher(IOptions<RabbitMqOptions> options) : IDisposable
{
    private readonly object _gate = new();
    private IConnection? _conn;
    private IModel? _ch;
    private volatile bool _returned;

    public void Publish<T>(string routingKey, T message, string? correlationId)
    {
        lock (_gate)
        {
            var ch = Channel();
            var props = ch.CreateBasicProperties();
            props.Persistent = true;
            props.ContentType = "application/json";
            props.MessageId = Guid.NewGuid().ToString();
            props.Headers = new Dictionary<string, object> { [Correlation.Header] = correlationId ?? "" };

            _returned = false;
            ch.BasicPublish(RabbitTopology.Exchange, routingKey, mandatory: true, props,
                JsonSerializer.SerializeToUtf8Bytes(message, Json.Options));
            ch.WaitForConfirmsOrDie(TimeSpan.FromSeconds(5));   // throws if broker nacks/times out
            if (_returned) throw new InvalidOperationException($"No queue is bound for routing key '{routingKey}'.");
        }
    }

    private IModel Channel()
    {
        if (_ch is { IsOpen: true }) return _ch;
        _ch?.Dispose();
        _conn?.Dispose();
        _conn = options.Value.ToFactory().CreateConnection("publisher");
        _ch = _conn.CreateModel();
        _ch.ConfirmSelect();
        _ch.BasicReturn += (_, _) => _returned = true;
        RabbitTopology.DeclareExchanges(_ch);
        return _ch;
    }

    public void Dispose() { _ch?.Dispose(); _conn?.Dispose(); }
}
