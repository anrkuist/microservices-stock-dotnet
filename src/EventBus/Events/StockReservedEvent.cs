namespace EventBus.Events;

/// <summary>
/// Published by Stock.API when inventory is successfully reserved.
/// Sales.API listens to this to confirm the order.
/// </summary>
public record StockReservedEvent : IntegrationEvent
{
    public int OrderId { get; init; }
    public int ProductId { get; init; }
    public int QuantityReserved { get; init; }
}
