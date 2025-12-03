using Microsoft.EntityFrameworkCore;
using Sales.API.Models;

namespace Sales.API.Data;

public class SalesDbContext : DbContext
{
    public SalesDbContext(DbContextOptions<SalesDbContext> options) : base(options) { }

    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configurar precisão decimal para preços
        modelBuilder.Entity<Order>()
            .Property(o => o.UnitPrice)
            .HasPrecision(18, 2);

        // Índice para consultas por usuário
        modelBuilder.Entity<Order>()
            .HasIndex(o => o.UserId);

        // Índice para consultas por status
        modelBuilder.Entity<Order>()
            .HasIndex(o => o.Status);
    }
}