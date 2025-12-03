using EventBus.Events;
using EventBusRabbitMQ;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sales.API.Data;
using Sales.API.Models;
using Sales.API.Services;
using System.Security.Claims;

namespace Sales.API.Controllers;

[ApiController]
[Route("[controller]")]
[Authorize] // Todos os endpoints requerem autenticação
public class OrdersController : ControllerBase
{
    private readonly SalesDbContext _context;
    private readonly RabbitMQEventBus _eventBus;
    private readonly ILogger<OrdersController> _logger;
    private readonly IStockService _stockService;

    public OrdersController(
        SalesDbContext context,
        RabbitMQEventBus eventBus,
        ILogger<OrdersController> logger,
        IStockService stockService)
    {
        _context = context;
        _eventBus = eventBus;
        _logger = logger;
        _stockService = stockService;
    }

    // Pega todos os pedidos do usuário logado
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Order>>> GetOrders()
    {
        var userId = GetUserId();
        var orders = await _context.Orders
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        return Ok(orders);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Order>> GetOrder(int id)
    {
        var userId = GetUserId();
        var order = await _context.Orders
            .FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId);

        if (order == null)
        {
            return NotFound(new { message = "Ops, não achei esse pedido no banco" });
        }

        return Ok(order);
    }

    // TODO: Adicionar validação de quantidade mínima/máxima depois
    [HttpPost]
    public async Task<ActionResult<Order>> CreateOrder(CreateOrderRequest request)
    {
        var userId = GetUserId();
        var username = GetUsername();

        _logger.LogInformation("Criando pedido pro produto {ProductId}", request.ProductId);

        // Antes de criar o pedido, a gente precisa bater no serviço de estoque
        // pra garantir que o produto existe e tem quantidade.
        // Se não tiver, já retorna erro pro cliente na hora.
        var hasStock = await _stockService.ValidateStockAsync(request.ProductId, request.Quantity);

        if (!hasStock)
        {
            _logger.LogWarning(
                "Deu ruim: não tem estoque suficiente pro produto {ProductId} (pediu {Quantity})",
                request.ProductId, request.Quantity);

            return BadRequest(new { message = "Ih, não tem estoque suficiente ou o produto não existe" });
        }

        // Show, tem estoque! Agora sim podemos criar o pedido
        // Já coloco como Confirmed porque a validação passou
        var order = new Order
        {
            ProductId = request.ProductId,
            Quantity = request.Quantity,
            UnitPrice = request.Price,
            Status = "Confirmed",
            UserId = userId,
            Username = username
        };

        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Pedido #{OrderId} criado com sucesso!", order.Id);

        // Manda pro RabbitMQ pra descontar o estoque lá no Stock.API
        // Isso roda em background, não trava a resposta pro cliente
        await _eventBus.PublishAsync(new OrderCreatedEvent
        {
            OrderId = order.Id,
            ProductId = request.ProductId,
            Quantity = request.Quantity,
            Price = request.Price
        });

        _logger.LogInformation("Evento enviado pro RabbitMQ, pedido #{OrderId}", order.Id);

        return CreatedAtAction(
            nameof(GetOrder),
            new { id = order.Id },
            order);
    }

    // Cancela pedido - só funciona se ainda tiver pendente
    // TODO: Implementar estorno de estoque quando cancelar
    [HttpPut("{id}/cancel")]
    public async Task<IActionResult> CancelOrder(int id)
    {
        var userId = GetUserId();
        var order = await _context.Orders
            .FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId);

        if (order == null)
        {
            return NotFound(new { message = "Pedido não encontrado" });
        }

        if (order.Status != "Pending")
        {
            return BadRequest(new { message = $"Não dá pra cancelar, o pedido já tá {order.Status}" });
        }

        order.Status = "Cancelled";
        order.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Pedido #{OrderId} cancelado pelo usuário {UserId}", id, userId);

        return NoContent();
    }

    // Estatísticas básicas dos pedidos do usuário
    // TODO: Acho que dá pra cachear isso com Redis futuramente
    [HttpGet("stats")]
    public async Task<ActionResult> GetOrderStats()
    {
        var userId = GetUserId();
        var orders = await _context.Orders
            .Where(o => o.UserId == userId)
            .ToListAsync();

        var stats = new
        {
            total = orders.Count,
            pending = orders.Count(o => o.Status == "Pending"),
            confirmed = orders.Count(o => o.Status == "Confirmed"),
            rejected = orders.Count(o => o.Status == "Rejected"),
            cancelled = orders.Count(o => o.Status == "Cancelled"),
            totalSpent = orders.Where(o => o.Status == "Confirmed").Sum(o => o.TotalPrice)
        };

        return Ok(stats);
    }

    // Helpers pra pegar info do usuário logado
    private int GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.Parse(userIdClaim ?? "0");
    }

    private string GetUsername()
    {
        return User.FindFirst(ClaimTypes.Name)?.Value ?? "Unknown";
    }
}

public record CreateOrderRequest(int ProductId, int Quantity, decimal Price);