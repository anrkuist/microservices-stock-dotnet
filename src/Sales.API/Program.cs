using Sales.API.Data;
using Sales.API.Hubs;
using Sales.API.Services;
using EventBus.Events;
using EventBusRabbitMQ;
using HealthChecks.UI.Client;
using RabbitMQ.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
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
    .Enrich.WithProperty("Service", "Sales.API")
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{Service}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.Seq(Environment.GetEnvironmentVariable("Seq__Url") ?? "http://localhost:5341")
    .CreateLogger();

try
{
    Log.Information("Iniciando Sales.API...");

    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();

    // Database
    builder.Services.AddDbContext<SalesDbContext>(options =>
        options.UseSqlServer(
            builder.Configuration.GetConnectionString("DefaultConnection")
            ?? "Server=localhost;Database=SalesDB;User Id=sa;Password=Your_password123;TrustServerCertificate=True"));

    // Authentication - MESMA CHAVE DO IDENTITY.API!
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

            // Configuração para SignalR - permite JWT via query string
            options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];
                    var path = context.HttpContext.Request.Path;

                    // Se a requisição for para o hub do SignalR e houver token na query string
                    if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/orders"))
                    {
                        context.Token = accessToken;
                    }

                    return Task.CompletedTask;
                }
            };
        });

    builder.Services.AddAuthorization();
    builder.Services.AddControllers();

    // HttpClient para comunicação síncrona com Stock.API (com Resiliência)
    builder.Services.AddHttpClient<IStockService, StockService>()
        .AddStandardResilienceHandler(); // Padrão Microsoft: retry + circuit breaker + timeout

    // === SignalR Configuration ===
    builder.Services.AddSignalR()
        .AddStackExchangeRedis(builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379", options =>
        {
            options.Configuration.ChannelPrefix = new StackExchange.Redis.RedisChannel("SalesAPI:", StackExchange.Redis.RedisChannel.PatternMode.Literal);
        });

    // Health Checks - monitora SQL Server, RabbitMQ e Redis
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? "Server=localhost;Database=SalesDB;User Id=sa;Password=Your_password123;TrustServerCertificate=True";
    var redisConnection = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
    var rabbitHost = builder.Configuration.GetValue<string>("RabbitMQ:Host") ?? "localhost";

    builder.Services.AddHealthChecks()
        .AddSqlServer(
            connectionString: connectionString,
            name: "sqlserver",
            failureStatus: HealthStatus.Unhealthy,
            tags: new[] { "db", "sql", "sqlserver" })
        .AddRabbitMQ(
            rabbitConnectionString: $"amqp://guest:guest@{rabbitHost}:5672",
            name: "rabbitmq",
            failureStatus: HealthStatus.Degraded, // Degraded pq funciona sem RabbitMQ
            tags: new[] { "messaging", "rabbitmq" })
        .AddRedis(
            redisConnectionString: redisConnection,
            name: "redis",
            failureStatus: HealthStatus.Degraded, // SignalR funciona local sem Redis
            tags: new[] { "cache", "redis", "signalr" });

    // CORS para permitir conexões SignalR do frontend
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("SignalRCorsPolicy", builder =>
        {
            builder.WithOrigins("http://localhost:5173", "http://localhost:3000") // Ajuste conforme seu frontend
                   .AllowAnyHeader()
                   .AllowAnyMethod()
                   .AllowCredentials(); // Necessário para SignalR
        });
    });

    // Swagger
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new() { Title = "Sales API", Version = "v1" });

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
    // Sales.API uses synchronous HTTP validation (no event publishing needed)
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

    var app = builder.Build();

    // Apply migrations automatically
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
        db.Database.Migrate();
    }

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseCors("SignalRCorsPolicy"); // Antes de UseAuthentication
    app.UseHttpsRedirection();
    app.UseAuthentication(); // IMPORTANTE: antes de UseAuthorization
    app.UseAuthorization();
    app.MapControllers();

    // Mapear o Hub do SignalR
    app.MapHub<OrderHub>("/hubs/orders");

    // Health Check Endpoints
    app.MapHealthChecks("/health", new HealthCheckOptions
    {
        ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
    });

    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("db"),
        ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
    });

    app.MapHealthChecks("/health/live", new HealthCheckOptions
    {
        Predicate = _ => false
    });

    app.MapGet("/", () => "Sales API is running 💰").AllowAnonymous();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Sales.API terminou inesperadamente");
}
finally
{
    Log.CloseAndFlush();
}