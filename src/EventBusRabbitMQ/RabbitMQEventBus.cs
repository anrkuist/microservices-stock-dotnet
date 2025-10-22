using EventBus.Events;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;

namespace EventBusRabbitMQ;

public class RabbitMQEventBus : IAsyncDisposable
{
    private readonly IConnection _connection;
    private bool _disposed;

    public RabbitMQEventBus(string hostName = "localhost")
    {
        var factory = new ConnectionFactory()
        {
            HostName = hostName
        };

        // CreateConnectionAsync is now the standard method
        _connection = factory.CreateConnectionAsync().GetAwaiter().GetResult();
    }

    public static async Task<RabbitMQEventBus> CreateAsync(string hostName = "localhost")
    {
        var factory = new ConnectionFactory()
        {
            HostName = hostName
        };

        var connection = await factory.CreateConnectionAsync();
        return new RabbitMQEventBus(connection);
    }

    private RabbitMQEventBus(IConnection connection)
    {
        _connection = connection;
    }

    public async Task PublishAsync(IntegrationEvent @event, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(RabbitMQEventBus));

        // CreateChannelAsync replaces CreateModel
        var channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);

        try
        {
            var queue = @event.GetType().Name;

            // QueueDeclareAsync is now async
            await channel.QueueDeclareAsync(
                queue: queue,
                durable: false,
                exclusive: false,
                autoDelete: false,
                arguments: null,
                cancellationToken: cancellationToken);

            var message = JsonSerializer.Serialize(@event);
            var body = Encoding.UTF8.GetBytes(message);

            // BasicProperties creation
            var properties = new BasicProperties
            {
                Persistent = false,
                ContentType = "application/json"
            };

            // BasicPublishAsync is now async
            await channel.BasicPublishAsync(
                exchange: "",
                routingKey: queue,
                mandatory: false,
                basicProperties: properties,
                body: body,
                cancellationToken: cancellationToken);
        }
        finally
        {
            await channel.CloseAsync(cancellationToken);
            await channel.DisposeAsync();
        }
    }

    // Synchronous wrapper for backward compatibility
    public void Publish(IntegrationEvent @event)
    {
        PublishAsync(@event).GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        if (_connection != null)
        {
            await _connection.CloseAsync();
            await _connection.DisposeAsync();
        }

        _disposed = true;
    }
}