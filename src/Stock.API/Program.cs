using Microsoft.EntityFrameworkCore;
using Stock.API.Data;
using Stock.API.EventHandlers;
using EventBus.Abstractions;
using EventBus.Events;
using EventBus.HostedServices;
using EventBusRabbitMQ;
using HealthChecks.UI.Client;
using RabbitMQ.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System.Text;

// Configura Serilog antes de tudo
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "Stock.API")
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{Service}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.Seq(Environment.GetEnvironmentVariable("Seq__Url") ?? "http://localhost:5341")
    .CreateLogger();

try
{
    Log.Information("Iniciando Stock.API...");

    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();

    // Database
    builder.Services.AddDbContext<StockDbContext>(opt =>
        opt.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

    // Authentication - MESMA CHAVE!
    var secretKey = builder.Configuration["Jwt:SecretKey"] ?? "secret-key-poc-12345-ecommerce-microservices";
    builder.Services.AddAuthentication("Bearer")
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "ecommerce-api",
                ValidAudience = builder.Configuration["Jwt:Audience"] ?? "ecommerce-clients",
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey))
            };
        });

    builder.Services.AddAuthorization(options =>
    {
        // Política: apenas Admin pode gerenciar produtos
        options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
    });

    builder.Services.AddControllers();

    // Health Checks - monitora SQL Server e RabbitMQ
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    var rabbitHost = builder.Configuration.GetValue<string>("RabbitMQ:Host") ?? "localhost";

    builder.Services.AddHealthChecks()
        .AddSqlServer(
            connectionString: connectionString!,
            name: "sqlserver",
            failureStatus: HealthStatus.Unhealthy,
            tags: new[] { "db", "sql", "sqlserver" })
        .AddRabbitMQ(
            rabbitConnectionString: $"amqp://guest:guest@{rabbitHost}:5672",
            name: "rabbitmq",
            failureStatus: HealthStatus.Unhealthy, // RabbitMQ é essencial pro Stock.API
            tags: new[] { "messaging", "rabbitmq" });

    // Swagger
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new() { Title = "Stock API", Version = "v1" });

        c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
        {
            Description = "JWT Authorization header. Enter: Bearer {your token}",
            Name = "Authorization",
            In = Microsoft.OpenApi.Models.ParameterLocation.Header,
            Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
            Scheme = "Bearer"
        });

        c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
        {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
        });
    });

    // === Event Bus Configuration ===
    // Conexão lazy com retry - não bloqueia a inicialização
    builder.Services.AddSingleton<IConnection>(sp =>
    {
        var factory = new ConnectionFactory()
        {
            HostName = builder.Configuration.GetValue<string>("RabbitMQ:Host") ?? "localhost"
        };

        // Retry com backoff exponencial
        var maxRetries = 5;
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                Log.Information("Tentando conectar ao RabbitMQ... (tentativa {Attempt}/{MaxRetries})", i + 1, maxRetries);
                var connection = factory.CreateConnectionAsync().GetAwaiter().GetResult();
                Log.Information("Conectado ao RabbitMQ com sucesso!");
                return connection;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Falha ao conectar ao RabbitMQ. Tentativa {Attempt}/{MaxRetries}", i + 1, maxRetries);
                if (i == maxRetries - 1) throw;
                Thread.Sleep(TimeSpan.FromSeconds(Math.Pow(2, i))); // 1s, 2s, 4s, 8s, 16s
            }
        }
        throw new InvalidOperationException("Não foi possível conectar ao RabbitMQ");
    });

    builder.Services.AddSingleton<RabbitMQEventBus>(sp =>
    {
        var connection = sp.GetRequiredService<IConnection>();
        return new RabbitMQEventBus(connection);
    });

    builder.Services.AddSingleton<RabbitMQEventBusConsumer>(sp =>
    {
        var connection = sp.GetRequiredService<IConnection>();
        return new RabbitMQEventBusConsumer(connection, sp);
    });

    builder.Services.AddScoped<IIntegrationEventHandler<OrderCreatedEvent>, OrderCreatedEventHandler>();

    builder.Services.AddHostedService(sp =>
    {
        var consumer = sp.GetRequiredService<RabbitMQEventBusConsumer>();

        async Task SubscribeToEvents()
        {
            await consumer.SubscribeAsync<OrderCreatedEvent, OrderCreatedEventHandler>();
        }

        return new EventBusHostedService(SubscribeToEvents);
    });

    var app = builder.Build();

    // Apply migrations
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<StockDbContext>();
        db.Database.Migrate();
    }

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();

    // Health Check Endpoints
    app.MapHealthChecks("/health", new HealthCheckOptions
    {
        ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
    });

    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("db") || check.Tags.Contains("messaging"),
        ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
    });

    app.MapHealthChecks("/health/live", new HealthCheckOptions
    {
        Predicate = _ => false
    });

    app.MapGet("/", () => "Stock API is running 📦").AllowAnonymous();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Stock.API terminou inesperadamente");
}
finally
{
    Log.CloseAndFlush();
}