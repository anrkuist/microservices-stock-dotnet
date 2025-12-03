using FluentAssertions;
using Sales.API.Models;
using Xunit;

namespace Sales.API.Tests.Models;

/// <summary>
/// Testes pro modelo Order
/// </summary>
public class OrderTests
{
    [Fact]
    public void Order_DeveInicializarComValoresPadrao()
    {
        // Act
        var order = new Order();

        // Assert
        order.Id.Should().Be(0);
        order.ProductId.Should().Be(0);
        order.Quantity.Should().Be(0);
        order.UnitPrice.Should().Be(0);
        order.Status.Should().Be("Pending");
        order.Username.Should().Be("");
        order.RejectionReason.Should().BeNull();
    }

    [Fact]
    public void Order_TotalPrice_CalculaCorretamente()
    {
        // Arrange
        var order = new Order
        {
            UnitPrice = 99.90m,
            Quantity = 3
        };

        // Act & Assert
        order.TotalPrice.Should().Be(299.70m);
    }

    [Theory]
    [InlineData(100, 1, 100)]
    [InlineData(50, 2, 100)]
    [InlineData(33.33, 3, 99.99)]
    [InlineData(0.01, 100, 1)]
    public void Order_TotalPrice_CalculaParaDiferentesQuantidades(decimal unitPrice, int quantity, decimal expectedTotal)
    {
        // Arrange
        var order = new Order
        {
            UnitPrice = unitPrice,
            Quantity = quantity
        };

        // Act & Assert
        order.TotalPrice.Should().Be(expectedTotal);
    }

    [Fact]
    public void Order_CreatedAt_DeveSerDefinidoAutomaticamente()
    {
        // Arrange
        var antes = DateTime.UtcNow.AddSeconds(-1);

        // Act
        var order = new Order();
        var depois = DateTime.UtcNow.AddSeconds(1);

        // Assert
        order.CreatedAt.Should().BeAfter(antes);
        order.CreatedAt.Should().BeBefore(depois);
    }

    [Fact]
    public void Order_UpdatedAt_DeveSerNuloPorPadrao()
    {
        // Act
        var order = new Order();

        // Assert
        order.UpdatedAt.Should().BeNull();
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Confirmed")]
    [InlineData("Rejected")]
    [InlineData("Cancelled")]
    public void Order_Status_AceitaValoresValidos(string status)
    {
        // Arrange & Act
        var order = new Order { Status = status };

        // Assert
        order.Status.Should().Be(status);
    }

    [Fact]
    public void Order_RejectionReason_PodeSerDefinido()
    {
        // Arrange
        var order = new Order
        {
            Status = "Rejected",
            RejectionReason = "Estoque insuficiente"
        };

        // Assert
        order.RejectionReason.Should().Be("Estoque insuficiente");
    }

    [Fact]
    public void Order_DeveArmazenarDadosDoUsuario()
    {
        // Arrange & Act
        var order = new Order
        {
            UserId = 42,
            Username = "joao.silva"
        };

        // Assert
        order.UserId.Should().Be(42);
        order.Username.Should().Be("joao.silva");
    }
}
