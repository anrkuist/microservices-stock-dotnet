using System.Text;
using System.Text.Json;

namespace Sales.API.Services;

public interface IStockService
{
    Task<bool> ValidateStockAsync(int productId, int quantity);
}

public class StockService : IStockService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<StockService> _logger;
    private readonly string _baseUrl;

    public StockService(HttpClient httpClient, ILogger<StockService> logger, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _logger = logger;
        _baseUrl = configuration["StockService:BaseUrl"] ?? "http://localhost:5216";
    }

    public async Task<bool> ValidateStockAsync(int productId, int quantity)
    {
        try
        {
            var content = new StringContent(
                JsonSerializer.Serialize(new { productId, quantity }),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync($"{_baseUrl}/Products/validate", content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Validação de estoque falhou: {StatusCode} - {Error}", response.StatusCode, error);
                return false;
            }

            _logger.LogInformation("Estoque validado pro produto {ProductId}, quantidade {Quantity}", productId, quantity);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao comunicar com Stock API pro produto {ProductId}", productId);
            return false;
        }
    }
}
