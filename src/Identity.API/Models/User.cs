namespace Identity.API.Models;

/// <summary>
/// Modelo de usuário para autenticação
/// Em produção, use ASP.NET Identity com hash de senha adequado
/// </summary>
public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = ""; // Em produção, use BCrypt ou similar
    public string Role { get; set; } = "Customer"; // Customer, Admin
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}