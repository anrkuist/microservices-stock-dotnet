using FluentAssertions;
using Stock.API.Models;
using Xunit;

namespace Stock.API.Tests.Models;

/// <summary>
/// Testes pro modelo Product
/// </summary>
public class ProductTests
{
    [Fact]
    public void Product_DeveInicializarComValoresPadrao()
    {
        // Act
        var product = new Product();

        // Assert
        product.Id.Should().Be(0);
        product.Name.Should().Be("");
        product.Description.Should().Be("");
        product.Price.Should().Be(0);
        product.Quantity.Should().Be(0);
    }

    [Fact]
    public void Product_DeveArmazenarValoresCorretamente()
    {
        // Arrange & Act
        var product = new Product
        {
            Id = 1,
            Name = "Notebook Dell",
            Description = "i7, 16GB RAM",
            Price = 4999.99m,
            Quantity = 15
        };

        // Assert
        product.Id.Should().Be(1);
        product.Name.Should().Be("Notebook Dell");
        product.Description.Should().Be("i7, 16GB RAM");
        product.Price.Should().Be(4999.99m);
        product.Quantity.Should().Be(15);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(999999)]
    public void Product_QuantidadeAceitaValoresPositivos(int quantidade)
    {
        // Arrange & Act
        var product = new Product { Quantity = quantidade };

        // Assert
        product.Quantity.Should().Be(quantidade);
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(99.99)]
    [InlineData(99999.99)]
    public void Product_PrecoAceitaDecimais(decimal preco)
    {
        // Arrange & Act
        var product = new Product { Price = preco };

        // Assert
        product.Price.Should().Be(preco);
    }
}
