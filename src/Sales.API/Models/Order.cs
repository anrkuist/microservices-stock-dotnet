namespace Sales.API.Models;

// Representa um pedido
public class Order
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalPrice => UnitPrice * Quantity;

    // Status possíveis: Pending, Confirmed, Rejected, Cancelled
    public string Status { get; set; } = "Pending";

    public int UserId { get; set; }
    public string Username { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Motivo se o pedido for rejeitado
    public string? RejectionReason { get; set; }
}