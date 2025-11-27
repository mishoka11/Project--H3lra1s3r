using Microsoft.EntityFrameworkCore;
using OrderService.Infra;
using Prometheus;
using Serilog;
using Polly;
using static H3lRa1s3r.Api.OrderService.Models;

var builder = WebApplication.CreateBuilder(args);

// Logging
builder.Host.UseSerilog((ctx, cfg) =>
    cfg.ReadFrom.Configuration(ctx.Configuration).WriteTo.Console());

// --- Build secure connection string from ENV variables ---
var host = Environment.GetEnvironmentVariable("POSTGRES_HOST")
           ?? "postgres.h3llra1s3r.svc.cluster.local";

var dbname = Environment.GetEnvironmentVariable("POSTGRES_DB") ?? "h3db";
var user = Environment.GetEnvironmentVariable("POSTGRES_USER");
var pass = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD");

var conn = $"Host={host};Port=5432;Database={dbname};Username={user};Password={pass};Pooling=true;";

// EF Core
builder.Services.AddDbContext<OrderDbContext>(o => o.UseNpgsql(conn));
builder.Services.AddHealthChecks();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// DB Migrations with Retry
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();

    var retryPolicy = Policy
        .Handle<Exception>()
        .WaitAndRetry(5, _ => TimeSpan.FromSeconds(3),
            (ex, _) => Console.WriteLine($"⏳ Waiting for DB... ({ex.Message})"));

    retryPolicy.Execute(() =>
    {
        Console.WriteLine("🔍 Applying EF Core migrations...");
        db.Database.Migrate();
        Console.WriteLine("✅ Database ready!");
    });
}

// Middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();
app.UseCors();

// Prometheus
app.UseMetricServer();
app.UseHttpMetrics();

// API
app.MapGet("/api/v1/orders", async (OrderDbContext dbCtx) =>
    Results.Ok(await dbCtx.Orders.ToListAsync()));

app.MapGet("/healthz/live", () => Results.Ok("Alive"));
app.MapGet("/healthz/ready", () => Results.Ok("Ready"));

app.Run();
