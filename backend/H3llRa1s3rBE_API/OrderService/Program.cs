using Prometheus;
using Serilog;
using static H3lRa1s3r.Api.OrderService.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) =>
    cfg.ReadFrom.Configuration(ctx.Configuration).WriteTo.Console());

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();
app.UseCors();
app.UseMetricServer();
app.UseHttpMetrics();

// ✅ Seed demo order
if (!OrdersDb.Orders.ContainsKey("1"))
{
    var demoOrder = new Order(
        Id: "1",
        UserId: "demo-user",
        CreatedAt: DateTimeOffset.UtcNow,
        Items: new[]
        {
            new OrderItem(ProductId: "demo-prod-001", Quantity: 2, UnitPrice: 19.99m),
            new OrderItem(ProductId: "demo-prod-002", Quantity: 1, UnitPrice: 39.99m)
        },
        Status: "Created"
    );

    OrdersDb.Orders["1"] = demoOrder;
}

// ---- Endpoints ----
app.MapPost("/api/v1/orders", (Order order, HttpRequest req) =>
{
    var key = req.Headers.TryGetValue("Idempotency-Key", out var k)
        ? k.ToString()
        : order.Id;

    if (OrdersDb.Orders.ContainsKey(key))
        return Results.Ok(OrdersDb.Orders[key]);

    OrdersDb.Orders[key] = order with
    {
        CreatedAt = DateTimeOffset.UtcNow,
        Status = "Created"
    };

    return Results.Created($"/api/v1/orders/{key}", OrdersDb.Orders[key]);
})
.WithName("CreateOrder")
.WithOpenApi();

app.MapGet("/api/v1/orders/{id}", (string id) =>
    OrdersDb.Orders.TryGetValue(id, out var o)
        ? Results.Ok(o)
        : Results.NotFound())
.WithName("GetOrder")
.WithOpenApi();

app.MapHealthChecks("/healthz/live");
app.MapHealthChecks("/healthz/ready");

app.Run();
