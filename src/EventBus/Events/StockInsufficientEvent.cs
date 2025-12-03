namespace EventBus.Events;

/// <summary>
/// Published by Stock.API when there's insufficient inventory.
/// Sales.API listens to this to cancel/reject the order.
/// </summary>
public record StockInsufficientEvent : IntegrationEvent
{
    public int OrderId { get; init; }
    public int ProductId { get; init; }
    public int RequestedQuantity { get; init; }
    public int AvailableQuantity { get; init; }
    public string Reason { get; init; } = "";
}