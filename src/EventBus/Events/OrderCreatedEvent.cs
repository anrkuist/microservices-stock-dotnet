namespace EventBus.Events;

/// <summary>
/// Published by Sales.API when a new order is created.
/// Stock.API listens to this to reserve inventory.
/// </summary>
public record OrderCreatedEvent : IntegrationEvent
{
    public int OrderId { get; init; }
    public int ProductId { get; init; }
    public int Quantity { get; init; }
    public decimal Price { get; init; }
}