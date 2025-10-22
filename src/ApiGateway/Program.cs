var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "API Gateway ativo 🚪");

// Simplesmente redireciona para os serviços
app.Map("/{service}/{**rest}", async (HttpContext ctx, string service, string rest) =>
{
    var client = new HttpClient();
    var target = service switch
    {
        "sales" => $"http://localhost:5010/{rest}",
        "stock" => $"http://localhost:5020/{rest}",
        _ => null
    };

    if (target == null)
    {
        ctx.Response.StatusCode = 404;
        await ctx.Response.WriteAsync("Serviço não encontrado");
        return;
    }

    var response = await client.GetAsync(target);
    ctx.Response.StatusCode = (int)response.StatusCode;
    await response.Content.CopyToAsync(ctx.Response.Body);
});

app.Run();
