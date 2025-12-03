using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Stock.API.Controllers;
using Stock.API.Data;
using Stock.API.Models;
using Xunit;

namespace Stock.API.Tests.Controllers;

/// <summary>
/// Testes pro ProductsController
/// </summary>
public class ProductsControllerTests : IDisposable
{
    private readonly StockDbContext _context;
    private readonly ProductsController _controller;
    private readonly Mock<ILogger<ProductsController>> _loggerMock;

    public ProductsControllerTests()
    {
        // Usa banco em memória pra cada teste
        var options = new DbContextOptionsBuilder<StockDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new StockDbContext(options);
        _loggerMock = new Mock<ILogger<ProductsController>>();
        _controller = new ProductsController(_context, _loggerMock.Object);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    #region GetProducts

    [Fact]
    public async Task GetProducts_QuandoNaoTemProdutos_RetornaListaVazia()
    {
        // Act
        var result = await _controller.GetProducts();

        // Assert
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task GetProducts_QuandoTemProdutos_RetornaTodos()
    {
        // Arrange
        await SeedProdutos(3);

        // Act
        var result = await _controller.GetProducts();

        // Assert
        result.Value.Should().HaveCount(3);
    }

    #endregion

    #region GetProduct

    [Fact]
    public async Task GetProduct_QuandoExiste_RetornaProduto()
    {
        // Arrange
        var product = new Product { Name = "Teclado", Description = "Mecânico", Price = 299.90m, Quantity = 10 };
        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        // Act
        var result = await _controller.GetProduct(product.Id);

        // Assert
        result.Value.Should().NotBeNull();
        result.Value!.Name.Should().Be("Teclado");
        result.Value.Price.Should().Be(299.90m);
    }

    [Fact]
    public async Task GetProduct_QuandoNaoExiste_RetornaNotFound()
    {
        // Act
        var result = await _controller.GetProduct(999);

        // Assert
        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    #endregion

    #region CreateProduct

    [Fact]
    public async Task CreateProduct_ComDadosValidos_CriaProduto()
    {
        // Arrange
        var request = new CreateProductRequest("Mouse Gamer", "RGB lindão", 149.90m, 50);

        // Act
        var result = await _controller.CreateProduct(request);

        // Assert
        result.Result.Should().BeOfType<CreatedAtActionResult>();

        var createdResult = result.Result as CreatedAtActionResult;
        var product = createdResult!.Value as Product;

        product.Should().NotBeNull();
        product!.Name.Should().Be("Mouse Gamer");
        product.Price.Should().Be(149.90m);
        product.Quantity.Should().Be(50);
    }

    [Fact]
    public async Task CreateProduct_SalvaNoBanco()
    {
        // Arrange
        var request = new CreateProductRequest("Monitor", "4K", 1500m, 5);

        // Act
        await _controller.CreateProduct(request);

        // Assert
        var productNoBanco = await _context.Products.FirstAsync();
        productNoBanco.Name.Should().Be("Monitor");
    }

    #endregion

    #region UpdateProduct

    [Fact]
    public async Task UpdateProduct_QuandoExiste_AtualizaProduto()
    {
        // Arrange
        var product = new Product { Name = "Produto Velho", Description = "Desc", Price = 100m, Quantity = 10 };
        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        var request = new UpdateProductRequest("Produto Novo", "Desc Nova", 200m, 20);

        // Act
        var result = await _controller.UpdateProduct(product.Id, request);

        // Assert
        result.Should().BeOfType<NoContentResult>();

        var updated = await _context.Products.FindAsync(product.Id);
        updated!.Name.Should().Be("Produto Novo");
        updated.Price.Should().Be(200m);
        updated.Quantity.Should().Be(20);
    }

    [Fact]
    public async Task UpdateProduct_QuandoNaoExiste_RetornaNotFound()
    {
        // Arrange
        var request = new UpdateProductRequest("Nome", "Desc", 100m, 10);

        // Act
        var result = await _controller.UpdateProduct(999, request);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    #endregion

    #region DeleteProduct

    [Fact]
    public async Task DeleteProduct_QuandoExiste_RemoveProduto()
    {
        // Arrange
        var product = new Product { Name = "Pra Deletar", Description = "", Price = 10m, Quantity = 1 };
        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        // Act
        var result = await _controller.DeleteProduct(product.Id);

        // Assert
        result.Should().BeOfType<NoContentResult>();

        var exists = await _context.Products.AnyAsync(p => p.Id == product.Id);
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteProduct_QuandoNaoExiste_RetornaNotFound()
    {
        // Act
        var result = await _controller.DeleteProduct(999);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    #endregion

    #region ValidateStock

    [Fact]
    public async Task ValidateStock_ComEstoqueSuficiente_RetornaOk()
    {
        // Arrange
        var product = new Product { Name = "Produto", Description = "", Price = 50m, Quantity = 100 };
        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        var request = new ValidateStockRequest(product.Id, 10);

        // Act
        var result = await _controller.ValidateStock(request);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task ValidateStock_ComEstoqueInsuficiente_RetornaBadRequest()
    {
        // Arrange
        var product = new Product { Name = "Produto", Description = "", Price = 50m, Quantity = 5 };
        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        var request = new ValidateStockRequest(product.Id, 10); // Pedindo mais do que tem

        // Act
        var result = await _controller.ValidateStock(request);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ValidateStock_ProdutoInexistente_RetornaNotFound()
    {
        // Arrange
        var request = new ValidateStockRequest(999, 1);

        // Act
        var result = await _controller.ValidateStock(request);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task ValidateStock_QuantidadeExata_RetornaOk()
    {
        // Arrange - estoque exato ao pedido
        var product = new Product { Name = "Produto", Description = "", Price = 50m, Quantity = 10 };
        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        var request = new ValidateStockRequest(product.Id, 10);

        // Act
        var result = await _controller.ValidateStock(request);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    #endregion

    #region Helpers

    private async Task SeedProdutos(int quantidade)
    {
        for (int i = 1; i <= quantidade; i++)
        {
            _context.Products.Add(new Product
            {
                Name = $"Produto {i}",
                Description = $"Descrição do produto {i}",
                Price = 10m * i,
                Quantity = 10 * i
            });
        }
        await _context.SaveChangesAsync();
    }

    #endregion
}
