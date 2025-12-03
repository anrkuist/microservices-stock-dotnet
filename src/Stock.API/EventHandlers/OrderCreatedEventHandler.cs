using EventBus.Abstractions;
using EventBus.Events;
using Microsoft.EntityFrameworkCore;
using Stock.API.Data;

namespace Stock.API.EventHandlers;

// Handler que processa o evento quando um pedido é criado
// A validação já foi feita pelo Sales.API via HTTP, aqui só baixa o estoque
public class OrderCreatedEventHandler : IIntegrationEventHandler<OrderCreatedEvent>
{
    private readonly StockDbContext _context;
    private readonly ILogger<OrderCreatedEventHandler> _logger;

    public OrderCreatedEventHandler(
        StockDbContext context,
        ILogger<OrderCreatedEventHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task Handle(OrderCreatedEvent @event)
    {
        _logger.LogInformation(
            "Recebeu evento de pedido criado: Pedido #{OrderId}, Produto #{ProductId}, Qtd {Quantity}",
            @event.OrderId, @event.ProductId, @event.Quantity);

        // Busca o produto
        var product = await _context.Products
            .FirstOrDefaultAsync(p => p.Id == @event.ProductId);

        if (product == null)
        {
            // Isso não deveria acontecer nunca, a validação já foi feita antes
            _logger.LogError(
                "Eita, produto {ProductId} sumiu na hora de baixar estoque do pedido #{OrderId}",
                @event.ProductId, @event.OrderId);
            return;
        }

        // Verificação paranóica - se alguém comprou no meio do caminho
        // TODO: Implementar lock otimista pra evitar race condition
        if (product.Quantity < @event.Quantity)
        {
            _logger.LogError(
                "Race condition! Produto {ProductId} só tem {Available} mas pedido #{OrderId} precisa de {Requested}",
                @event.ProductId, product.Quantity, @event.OrderId, @event.Quantity);
            return;
        }

        // Baixa o estoque
        product.Quantity -= @event.Quantity;
        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "Estoque atualizado! Produto #{ProductId} agora tem {NewQuantity} unidades",
            @event.ProductId, product.Quantity);
    }
}