using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Sales.API.Services;
using System.Net;
using Xunit;

namespace Sales.API.Tests.Services;

/// <summary>
/// Testes pro StockService - comunicação HTTP com Stock.API
/// </summary>
public class StockServiceTests
{
    private readonly Mock<ILogger<StockService>> _loggerMock;
    private readonly IConfiguration _configuration;

    public StockServiceTests()
    {
        _loggerMock = new Mock<ILogger<StockService>>();

        // Configuração fake
        var inMemorySettings = new Dictionary<string, string?>
        {
            {"StockService:BaseUrl", "http://fake-stock-api"}
        };

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();
    }

    [Fact]
    public async Task ValidateStockAsync_QuandoRetorna200_RetornaTrue()
    {
        // Arrange
        var httpClient = CriarHttpClientMock(HttpStatusCode.OK, "{\"valid\": true}");
        var service = new StockService(httpClient, _loggerMock.Object, _configuration);

        // Act
        var result = await service.ValidateStockAsync(1, 5);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateStockAsync_QuandoRetorna400_RetornaFalse()
    {
        // Arrange
        var httpClient = CriarHttpClientMock(
            HttpStatusCode.BadRequest,
            "{\"message\": \"Estoque insuficiente\"}"
        );
        var service = new StockService(httpClient, _loggerMock.Object, _configuration);

        // Act
        var result = await service.ValidateStockAsync(1, 100);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateStockAsync_QuandoRetorna404_RetornaFalse()
    {
        // Arrange
        var httpClient = CriarHttpClientMock(
            HttpStatusCode.NotFound,
            "{\"message\": \"Produto não encontrado\"}"
        );
        var service = new StockService(httpClient, _loggerMock.Object, _configuration);

        // Act
        var result = await service.ValidateStockAsync(999, 1);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateStockAsync_QuandoExcecao_RetornaFalse()
    {
        // Arrange - HttpClient que joga exceção
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ThrowsAsync(new HttpRequestException("Erro de conexão"));

        var httpClient = new HttpClient(handlerMock.Object);
        var service = new StockService(httpClient, _loggerMock.Object, _configuration);

        // Act
        var result = await service.ValidateStockAsync(1, 1);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateStockAsync_QuandoTimeout_RetornaFalse()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ThrowsAsync(new TaskCanceledException("Timeout"));

        var httpClient = new HttpClient(handlerMock.Object);
        var service = new StockService(httpClient, _loggerMock.Object, _configuration);

        // Act
        var result = await service.ValidateStockAsync(1, 1);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateStockAsync_UsaUrlDaConfiguracao()
    {
        // Arrange
        Uri? capturedUri = null;

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedUri = req.RequestUri)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"valid\": true}")
            });

        var httpClient = new HttpClient(handlerMock.Object);
        var service = new StockService(httpClient, _loggerMock.Object, _configuration);

        // Act
        await service.ValidateStockAsync(42, 10);

        // Assert
        capturedUri.Should().NotBeNull();
        capturedUri!.ToString().Should().Contain("fake-stock-api");
        capturedUri.ToString().Should().Contain("/Products/validate");
    }

    [Fact]
    public async Task ValidateStockAsync_EnviaJsonCorreto()
    {
        // Arrange
        string? capturedBody = null;

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .Callback<HttpRequestMessage, CancellationToken>(async (req, _) =>
            {
                if (req.Content != null)
                    capturedBody = await req.Content.ReadAsStringAsync();
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"valid\": true}")
            });

        var httpClient = new HttpClient(handlerMock.Object);
        var service = new StockService(httpClient, _loggerMock.Object, _configuration);

        // Act
        await service.ValidateStockAsync(42, 10);

        // Assert
        capturedBody.Should().NotBeNull();
        capturedBody.Should().Contain("\"productId\":42");
        capturedBody.Should().Contain("\"quantity\":10");
    }

    #region Helpers

    private HttpClient CriarHttpClientMock(HttpStatusCode statusCode, string responseContent)
    {
        var handlerMock = new Mock<HttpMessageHandler>();

        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(responseContent)
            });

        return new HttpClient(handlerMock.Object);
    }

    #endregion
}
