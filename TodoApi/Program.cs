using Microsoft.EntityFrameworkCore;
using TodoApi.Data;
using TodoApi.Middleware;

var builder = WebApplication.CreateBuilder(args);
builder
    .Services.AddDbContext<TodoContext>(opt =>
        opt.UseSqlServer(builder.Configuration.GetConnectionString("TodoContext"))
    )
    .AddEndpointsApiExplorer()
    .AddControllers();

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var app = builder.Build();

app.UseMiddleware<GlobalErrorHandlingMiddleware>();

app.UseAuthorization();
app.MapControllers();
app.Run();
