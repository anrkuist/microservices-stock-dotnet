using Identity.API.Data;
using Identity.API.Models;
using Identity.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Identity.API.Controllers;

[ApiController]
[Route("[controller]")]
public class AuthController : ControllerBase
{
    private readonly IdentityDbContext _context;
    private readonly JwtService _jwtService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IdentityDbContext context,
        JwtService jwtService,
        ILogger<AuthController> logger)
    {
        _context = context;
        _jwtService = jwtService;
        _logger = logger;
    }

    // Cadastra usuário novo
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request)
    {
        // Verifica se a senha é forte e se o usuário já existe
        if (!_jwtService.ValidatePassword(request.Password))
        {
            return BadRequest(new { message = "Senha precisa ter pelo menos 6 caracteres" });
        }

        if (await _context.Users.AnyAsync(u => u.Username == request.Username))
        {
            return BadRequest(new { message = "Esse username já tá em uso" });
        }

        if (await _context.Users.AnyAsync(u => u.Email == request.Email))
        {
            return BadRequest(new { message = "Já tem uma conta com esse email" });
        }

        // Cria o usuário
        var user = new User
        {
            Username = request.Username,
            Email = request.Email,
            PasswordHash = _jwtService.HashPassword(request.Password),
            Role = "Customer" // Todo mundo começa como Customer
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Novo usuário cadastrado: {Username}", user.Username);

        // Gerar token
        var token = _jwtService.GenerateToken(user);

        return Ok(new AuthResponse
        {
            Token = token,
            Username = user.Username,
            Email = user.Email,
            Role = user.Role
        });
    }

    // Login - retorna o token JWT
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        // Busca por username ou email
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Username == request.Username || u.Email == request.Username);

        if (user == null)
        {
            return Unauthorized(new { message = "Usuário ou senha incorretos" });
        }

        // Confere a senha
        if (!_jwtService.VerifyPassword(request.Password, user.PasswordHash))
        {
            return Unauthorized(new { message = "Usuário ou senha incorretos" });
        }

        _logger.LogInformation("Login realizado: {Username}", user.Username);

        // Gerar token
        var token = _jwtService.GenerateToken(user);

        return Ok(new AuthResponse
        {
            Token = token,
            Username = user.Username,
            Email = user.Email,
            Role = user.Role
        });
    }

    // Retorna dados do usuário logado
    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UserInfo>> GetCurrentUser()
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        if (userId == null)
        {
            return Unauthorized();
        }

        var user = await _context.Users.FindAsync(int.Parse(userId));

        if (user == null)
        {
            return NotFound();
        }

        return Ok(new UserInfo
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            Role = user.Role
        });
    }
}

// DTOs
public record RegisterRequest(string Username, string Email, string Password);
public record LoginRequest(string Username, string Password);
public record AuthResponse
{
    public string Token { get; init; } = "";
    public string Username { get; init; } = "";
    public string Email { get; init; } = "";
    public string Role { get; init; } = "";
}
public record UserInfo
{
    public int Id { get; init; }
    public string Username { get; init; } = "";
    public string Email { get; init; } = "";
    public string Role { get; init; } = "";
}