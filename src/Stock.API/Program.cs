using Microsoft.EntityFrameworkCore;
using Stock.API.Data;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<StockDbContext>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddControllers();

var app = builder.Build();
app.MapControllers();
app.Run();
