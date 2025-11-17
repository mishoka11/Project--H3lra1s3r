using Microsoft.EntityFrameworkCore;
using OrderService.Infra;
using Prometheus;
using Serilog;
using Polly;
using static H3lRa1s3r.Api.OrderService.Models;

var builder = WebApplication.CreateBuilder(args);

// ---- Logging ----
builder.Host.UseSerilog((ctx, cfg) =>
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .WriteTo.Console());

// ---- Services ----
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ---- PostgreSQL + EF Core ----
var conn = builder.Configuration.GetConnectionString("Orders")
           ?? "Host=postgres.h3llra1s3r.svc.cluster.local;Port=5432;Database=h3db;Username=h3user;Password=h3pass;Pooling=true;";

builder.Services.AddDbContext<OrderDbContext>(o => o.UseNpgsql(conn));

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddHealthChecks();

var app = builder.Build();

// ---- Ensure DB schema + run migrations ----
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();

    var retryPolicy = Policy
        .Handle<Exception>()
        .WaitAndRetry(5, attempt => TimeSpan.FromSeconds(3),
            (ex, time) => Console.WriteLine($"⏳ Waiting for DB... ({ex.Message})"));

    retryPolicy.Execute(() =>
    {
        Console.WriteLine("🔍 Applying pending EF Core migrations...");
        db.Database.Migrate();
        Console.WriteLine("✅ Database schema up to date!");
    });

    // ✅ Seed demo data if empty
    if (!db.Orders.Any())
    {
        Console.WriteLine("🌱 Seeding demo order...");
        db.Orders.Add(new Order
        {
            UserId = "demo-user",
            CreatedAt = DateTimeOffset.UtcNow,
            Items = new List<OrderItem>
            {
                new() { ProductId = "demo-prod-001", Quantity = 2, UnitPrice = 19.99m },
                new() { ProductId = "demo-prod-002", Quantity = 1, UnitPrice = 39.99m }
            },
            Status = "Created"
        });
        db.SaveChanges();
        Console.WriteLine("✅ Seeded demo order");
    }
}

// ---- Middleware ----
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();
app.UseCors();

// 👇 Prometheus middleware
app.UseMetricServer();
app.UseHttpMetrics();

// ---- Endpoints ----
app.MapGet("/api/v1/orders", async (OrderDbContext db) =>
    Results.Ok(await db.Orders.ToListAsync()));

app.MapGet("/healthz/live", () => Results.Ok("Alive"));
app.MapGet("/healthz/ready", () => Results.Ok("Ready"));

app.Run();

