using Microsoft.EntityFrameworkCore;
using OrderService.Infra;
using Prometheus;
using Serilog;
using static H3lRa1s3r.Api.OrderService.Models;

var builder = WebApplication.CreateBuilder(args);

// ---- Logging ----
builder.Host.UseSerilog((ctx, cfg) =>
    cfg.ReadFrom.Configuration(ctx.Configuration).WriteTo.Console());

// ---- Services ----
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// PostgreSQL + EF Core
var conn = builder.Configuration.GetConnectionString("Orders")
           ?? "Host=postgres;Database=h3db;Username=h3user;Password=h3pass";

builder.Services.AddDbContext<OrderDbContext>(o => o.UseNpgsql(conn));
builder.Services.AddHealthChecks().AddNpgSql(conn);

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// ---- Middleware ----
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();
app.UseCors();
app.UseMetricServer();
app.UseHttpMetrics();

// ---- Endpoints ----

// ✅ Create Order
app.MapPost("/api/v1/orders", async (Order order, OrderDbContext db, HttpRequest req) =>
{
    var key = req.Headers.TryGetValue("Idempotency-Key", out var k)
        ? k.ToString()
        : order.Id ?? Guid.NewGuid().ToString("n");

    var existing = await db.Orders.FindAsync(key);
    if (existing != null)
        return Results.Ok(existing);

    var newOrder = new Order
    {
        Id = key,
        UserId = order.UserId,
        CreatedAt = DateTimeOffset.UtcNow,
        Items = order.Items,
        Status = "Created"
    };

    db.Orders.Add(newOrder);
    await db.SaveChangesAsync();

    return Results.Created($"/api/v1/orders/{key}", newOrder);
})
.WithName("CreateOrder")
.WithOpenApi();

// ✅ Get Order by Id
app.MapGet("/api/v1/orders/{id}", async (string id, OrderDbContext db) =>
{
    var order = await db.Orders.FindAsync(id);
    return order is not null ? Results.Ok(order) : Results.NotFound();
})
.WithName("GetOrder")
.WithOpenApi();

// ✅ Get All Orders
app.MapGet("/api/v1/orders", async (OrderDbContext db) =>
    Results.Ok(await db.Orders.ToListAsync()))
.WithName("GetAllOrders")
.WithOpenApi();

// ✅ Health
app.MapHealthChecks("/healthz/live");
app.MapHealthChecks("/healthz/ready");

// ✅ Seed demo data
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
    db.Database.EnsureCreated();

    if (!db.Orders.Any())
    {
        var demoOrder = new Order
        {
            UserId = "demo-user",
            CreatedAt = DateTimeOffset.UtcNow,
            Items = new List<OrderItem>
            {
                new OrderItem { ProductId = "demo-prod-001", Quantity = 2, UnitPrice = 19.99m },
                new OrderItem { ProductId = "demo-prod-002", Quantity = 1, UnitPrice = 39.99m }
            },
            Status = "Created"
        };

        db.Orders.Add(demoOrder);
        db.SaveChanges();
        Console.WriteLine("✅ Seeded demo Order");
    }
}

app.Run();
