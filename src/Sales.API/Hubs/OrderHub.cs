using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace Sales.API.Hubs;

// Hub do SignalR pra mandar notificações em tempo real sobre pedidos
[Authorize]
public class OrderHub : Hub
{
    private readonly ILogger<OrderHub> _logger;

    public OrderHub(ILogger<OrderHub> logger)
    {
        _logger = logger;
    }

    // Cliente se inscreve pra receber updates de um pedido específico
    public async Task SubscribeToOrder(int orderId)
    {
        var userId = GetUserId();
        var groupName = $"order-{orderId}";

        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

        _logger.LogInformation(
            "User {UserId} subscribed to order {OrderId} notifications (ConnectionId: {ConnectionId})",
            userId, orderId, Context.ConnectionId);

        await Clients.Caller.SendAsync("Subscribed", new
        {
            orderId,
            message = $"Inscrito para notificações do pedido #{orderId}"
        });
    }

    // Para de receber notificações desse pedido
    public async Task UnsubscribeFromOrder(int orderId)
    {
        var userId = GetUserId();
        var groupName = $"order-{orderId}";

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);

        _logger.LogInformation(
            "User {UserId} unsubscribed from order {OrderId} notifications",
            userId, orderId);
    }

    // Quando o cliente conecta
    public override async Task OnConnectedAsync()
    {
        var userId = GetUserId();
        _logger.LogInformation("User {UserId} connected to OrderHub (ConnectionId: {ConnectionId})",
            userId, Context.ConnectionId);

        await base.OnConnectedAsync();
    }

    // Quando o cliente desconecta
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = GetUserId();

        if (exception != null)
        {
            _logger.LogWarning(exception, "User {UserId} disconnected with error", userId);
        }
        else
        {
            _logger.LogInformation("User {UserId} disconnected from OrderHub", userId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private int GetUserId()
    {
        var userIdClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.Parse(userIdClaim ?? "0");
    }
}
