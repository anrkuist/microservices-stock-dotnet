using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using HealthChecks.UI.Client;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// JWT pra validar os tokens
var secretKey = builder.Configuration["Jwt:SecretKey"] ?? "secret-key-poc-12345-ecommerce-microservices";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "ecommerce-api",
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "ecommerce-clients",
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

// Health Checks - verifica se os serviços downstream estão de pé
var identityUrl = builder.Configuration["ReverseProxy:Clusters:identity-cluster:Destinations:destination1:Address"] ?? "http://localhost:5171";
var salesUrl = builder.Configuration["ReverseProxy:Clusters:sales-cluster:Destinations:destination1:Address"] ?? "http://localhost:5154";
var stockUrl = builder.Configuration["ReverseProxy:Clusters:stock-cluster:Destinations:destination1:Address"] ?? "http://localhost:5216";

builder.Services.AddHealthChecks()
    .AddUrlGroup(
        new Uri($"{identityUrl}/health"),
        name: "identity-api",
        failureStatus: HealthStatus.Degraded,
        tags: new[] { "service", "identity" })
    .AddUrlGroup(
        new Uri($"{salesUrl}/health"),
        name: "sales-api",
        failureStatus: HealthStatus.Degraded,
        tags: new[] { "service", "sales" })
    .AddUrlGroup(
        new Uri($"{stockUrl}/health"),
        name: "stock-api",
        failureStatus: HealthStatus.Degraded,
        tags: new[] { "service", "stock" });

// Rate Limiting - limita requisições por IP
// Configurações podem ser customizadas no appsettings.json
builder.Services.AddRateLimiter(options =>
{
    // Política global: Fixed Window (janela fixa de tempo)
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 100, // Máximo 100 requests
                Window = TimeSpan.FromMinutes(1) // Por minuto
            }));

    // Política específica pra endpoints sensíveis (login, register)
    options.AddFixedWindowLimiter("auth", limiterOptions =>
    {
        limiterOptions.PermitLimit = 10; // Só 10 tentativas
        limiterOptions.Window = TimeSpan.FromMinutes(5); // A cada 5 minutos
        limiterOptions.AutoReplenishment = true;
        limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit = 2; // Máximo 2 na fila de espera
    });

    // Política pra criação de pedidos (evitar spam)
    options.AddSlidingWindowLimiter("orders", limiterOptions =>
    {
        limiterOptions.PermitLimit = 20; // 20 pedidos
        limiterOptions.Window = TimeSpan.FromMinutes(10); // A cada 10 minutos
        limiterOptions.SegmentsPerWindow = 2;
        limiterOptions.AutoReplenishment = true;
    });

    // O que fazer quando o limite é atingido
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/json";

        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfterValue)
            ? retryAfterValue.TotalSeconds
            : 60;

        context.HttpContext.Response.Headers.RetryAfter = retryAfter.ToString();

        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            error = "Muitas requisições! Calma aí... ⏳",
            message = $"Limite de requisições atingido. Tente novamente em {retryAfter:F0} segundos.",
            retryAfterSeconds = retryAfter
        }, cancellationToken);
    };
});

// YARP - carrega config do appsettings.json
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

// Rate Limiting deve vir antes da autenticação
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => "API Gateway ativo 🚪 (YARP + Rate Limiting)").AllowAnonymous();

// Health Check Endpoints - não precisa de autenticação
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
}).AllowAnonymous();

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("service"),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
}).AllowAnonymous();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
}).AllowAnonymous();

// Mapeia as rotas do YARP
app.MapReverseProxy();

app.Run();