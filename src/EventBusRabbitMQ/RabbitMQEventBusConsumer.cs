using EventBus.Abstractions;
using EventBus.Events;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;

namespace EventBusRabbitMQ;

// Classe que fica escutando as mensagens do RabbitMQ
// Quando chega uma mensagem, ela chama o handler certo pra processar
public class RabbitMQEventBusConsumer : IAsyncDisposable
{
    private readonly IConnection _connection;
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<string, Type> _eventTypes = new();
    private readonly List<IChannel> _channels = new();
    private bool _disposed;

    public RabbitMQEventBusConsumer(IConnection connection, IServiceProvider serviceProvider)
    {
        _connection = connection;
        _serviceProvider = serviceProvider;
    }

    // Se inscreve pra receber um tipo de evento
    // Exemplo: SubscribeAsync<OrderCreatedEvent, OrderCreatedEventHandler>()
    public async Task SubscribeAsync<TEvent, THandler>()
        where TEvent : IntegrationEvent
        where THandler : IIntegrationEventHandler<TEvent>
    {
        var eventName = typeof(TEvent).Name;

        // Guarda o tipo do evento pra deserializar depois
        _eventTypes[eventName] = typeof(TEvent);

        // Cria um canal só pra essa inscrição
        var channel = await _connection.CreateChannelAsync();
        _channels.Add(channel);

        // Declara a fila (mesma config do publisher)
        await channel.QueueDeclareAsync(
            queue: eventName,
            durable: false,
            exclusive: false,
            autoDelete: false,
            arguments: null);

        // Cria o consumer
        var consumer = new AsyncEventingBasicConsumer(channel);

        // Isso aqui roda toda vez que chega uma mensagem
        consumer.ReceivedAsync += async (model, ea) =>
        {
            try
            {
                // Pega o conteúdo da mensagem
                var body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);

                Console.WriteLine($"RabbitMQ: Chegou uma mensagem nova do tipo {eventName}");
                Console.WriteLine($"Conteúdo: {message}");

                // Converte o JSON pro objeto certo
                var eventType = _eventTypes[eventName];
                var integrationEvent = JsonSerializer.Deserialize(message, eventType) as TEvent;

                if (integrationEvent != null)
                {
                    // Pega o handler do container de DI
                    // Precisa criar um scope porque o DbContext é scoped
                    using var scope = _serviceProvider.CreateScope();
                    var handler = scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<TEvent>>();

                    // Executa o handler
                    await handler.Handle(integrationEvent);

                    Console.WriteLine($"Tudo certo, evento {eventName} processado!");
                }

                // Manda o ack pro RabbitMQ saber que processamos
                await channel.BasicAckAsync(deliveryTag: ea.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Eita, deu erro processando {eventName}: {ex.Message}");

                // Dá nack e manda de volta pra fila tentar de novo
                await channel.BasicNackAsync(
                    deliveryTag: ea.DeliveryTag,
                    multiple: false,
                    requeue: true);
            }
        };

        // Começa a consumir - autoAck false pra gente controlar quando confirmar
        await channel.BasicConsumeAsync(
            queue: eventName,
            autoAck: false,
            consumer: consumer);

        Console.WriteLine($"Escutando mensagens do tipo {eventName}...");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        foreach (var channel in _channels)
        {
            await channel.CloseAsync();
            await channel.DisposeAsync();
        }

        _disposed = true;
    }
}