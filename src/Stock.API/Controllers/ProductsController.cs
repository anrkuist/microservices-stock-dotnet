using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stock.API.Data;
using Stock.API.Models;

namespace Stock.API.Controllers;

[ApiController]
[Route("[controller]")]
public class ProductsController : ControllerBase
{
    private readonly StockDbContext _context;
    private readonly ILogger<ProductsController> _logger;

    public ProductsController(StockDbContext context, ILogger<ProductsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<Product>>> GetProducts()
    {
        return await _context.Products.ToListAsync();
    }

    [HttpGet("{id}")]
    [AllowAnonymous]
    public async Task<ActionResult<Product>> GetProduct(int id)
    {
        var product = await _context.Products.FindAsync(id);

        if (product == null)
        {
            return NotFound(new { message = "Produto não encontrado" });
        }

        return product;
    }

    // Só admin pode criar produto
    // TODO: Adicionar validação de preço mínimo
    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<Product>> CreateProduct(CreateProductRequest request)
    {
        var product = new Product
        {
            Name = request.Name,
            Description = request.Description,
            Price = request.Price,
            Quantity = request.Quantity
        };

        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Produto criado: #{ProductId} - {ProductName}", product.Id, product.Name);

        return CreatedAtAction(nameof(GetProduct), new { id = product.Id }, product);
    }

    // Atualiza produto - só admin
    [HttpPut("{id}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> UpdateProduct(int id, UpdateProductRequest request)
    {
        var product = await _context.Products.FindAsync(id);

        if (product == null)
        {
            return NotFound(new { message = "Produto não existe" });
        }

        product.Name = request.Name;
        product.Description = request.Description;
        product.Price = request.Price;
        product.Quantity = request.Quantity;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Produto #{ProductId} atualizado", product.Id);

        return NoContent();
    }

    // Deleta produto
    [HttpDelete("{id}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> DeleteProduct(int id)
    {
        var product = await _context.Products.FindAsync(id);

        if (product == null)
        {
            return NotFound(new { message = "Produto não encontrado pra deletar" });
        }

        _context.Products.Remove(product);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Produto #{ProductId} deletado", product.Id);

        return NoContent();
    }

    // Endpoint que o Sales.API chama pra verificar estoque
    // Não reserva nada, só valida se tem quantidade
    [HttpPost("validate")]
    [AllowAnonymous]
    public async Task<IActionResult> ValidateStock([FromBody] ValidateStockRequest request)
    {
        var product = await _context.Products.FindAsync(request.ProductId);

        if (product == null)
        {
            _logger.LogWarning("Validação falhou: produto {ProductId} não existe", request.ProductId);
            return NotFound(new { message = "Produto não encontrado" });
        }

        if (product.Quantity < request.Quantity)
        {
            _logger.LogWarning(
                "Estoque insuficiente pro produto {ProductId}. Tem: {Available}, Pediu: {Requested}",
                request.ProductId, product.Quantity, request.Quantity);

            return BadRequest(new
            {
                message = "Estoque insuficiente",
                available = product.Quantity,
                requested = request.Quantity
            });
        }

        _logger.LogInformation(
            "Validação OK: produto {ProductId} tem estoque. Disponível: {Available}",
            request.ProductId, product.Quantity);

        // Retorna 200 se tiver estoque suficiente
        return Ok(new { valid = true, available = product.Quantity });
    }
}

public record CreateProductRequest(string Name, string Description, decimal Price, int Quantity);
public record UpdateProductRequest(string Name, string Description, decimal Price, int Quantity);
public record ValidateStockRequest(int ProductId, int Quantity);