using Microsoft.EntityFrameworkCore;
using TodoApi.Configuration;
using TodoApi.Data;
using TodoApi.Hubs;
using TodoApi.Middleware;
using TodoApi.Services;
using TodoApi.Services.Sync;

var builder = WebApplication.CreateBuilder(args);
builder
    .Services.AddDbContext<TodoContext>(opt =>
        opt.UseSqlServer(builder.Configuration.GetConnectionString("TodoContext"))
    )
    .AddEndpointsApiExplorer()
    .AddControllers();

// SignalR
builder.Services.AddSignalR();

// Cors
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", corsPolicyBuilder =>
    {
        corsPolicyBuilder.WithOrigins("http://localhost:5173")
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

// Background job services
builder.Services.AddSingleton<IBackgroundJobQueue, BackgroundJobQueue>();
builder.Services.AddHostedService<BackgroundJobProcessor>();

// Sync services
builder.Services.Configure<ExternalTodoApiConfiguration>(
    builder.Configuration.GetSection("ExternalTodoApi")
);
builder.Services.AddHttpClient<IExternalTodoApiClient, ExternalTodoApiClient>();
builder.Services.AddScoped<ISyncService, SyncServicePull>();

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var app = builder.Build();

app.UseMiddleware<GlobalErrorHandlingMiddleware>();

app.UseCors("AllowFrontend");

app.UseAuthorization();
app.MapControllers();
app.MapHub<TodoProgressHub>("/hubs/todo-progress");
app.Run();
