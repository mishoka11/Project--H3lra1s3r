using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OrderService.Infra;
using Polly;
using Prometheus;
using Serilog;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using static H3lRa1s3r.Api.OrderService.Models;

var builder = WebApplication.CreateBuilder(args);

// ------------------------------------------------------
// Logging
// ------------------------------------------------------
builder.Host.UseSerilog((ctx, cfg) =>
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .WriteTo.Console());

// ------------------------------------------------------
// Database
// ------------------------------------------------------
var user = Environment.GetEnvironmentVariable("POSTGRES_USER");
var pass = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD");

var conn =
    $"Host=postgres-orders.h3llra1s3r.svc.cluster.local;Port=5432;Database=orderdb;Username={user};Password={pass};Pooling=true;";

builder.Services.AddDbContext<OrderDbContext>(o => o.UseNpgsql(conn));
builder.Services.AddHealthChecks();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

// ------------------------------------------------------
// JWT AUTHENTICATION
// ------------------------------------------------------
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new Exception("Jwt:Key missing in appsettings.json OR environment variables");

var signingKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
    System.Text.Encoding.UTF8.GetBytes(jwtKey)
);


builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey
        };
    });

builder.Services.AddAuthorization();

// ------------------------------------------------------
// Pub/Sub (publisher: orders-topic, subscriber: stock-events-sub)
// ------------------------------------------------------
var projectId = builder.Configuration["Gcp:ProjectId"] ?? "linear-pointer-479410-n7";

// Publish to orders-topic
var orderTopicId = builder.Configuration["Gcp:OrderTopic"] ?? "orders-topic";
var orderTopicName = TopicName.FromProjectTopic(projectId, orderTopicId);
Console.WriteLine($"🔗 PubSub publisher topic: {orderTopicName}");
PublisherClient orderPublisher = await PublisherClient.CreateAsync(orderTopicName);

// Subscribe from stock-events-sub
var stockSubId = builder.Configuration["Gcp:StockSubscription"] ?? "stock-events-sub";
var stockSubName = SubscriptionName.FromProjectSubscription(projectId, stockSubId);
Console.WriteLine($"🔔 PubSub subscriber: {stockSubName}");
SubscriberClient stockSubscriber = await SubscriberClient.CreateAsync(stockSubName);

// Register subscriber in DI
builder.Services.AddSingleton(stockSubscriber);
builder.Services.AddHostedService<StockEventsSubscriber>();

var app = builder.Build();

// ------------------------------------------------------
// Run migrations
// ------------------------------------------------------
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();

    var retryPolicy = Policy
        .Handle<Exception>()
        .WaitAndRetry(5, attempt => TimeSpan.FromSeconds(3),
            (ex, _) => Console.WriteLine($"⏳ Waiting for DB... ({ex.Message})"));

    retryPolicy.Execute(() =>
    {
        Console.WriteLine("🔍 Applying pending migrations...");
        db.Database.Migrate();
        Console.WriteLine("✅ DB Schema is up to date");
    });
}

// ------------------------------------------------------
// Middleware
// ------------------------------------------------------
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();
app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

app.UseMetricServer();
app.UseHttpMetrics();

// ------------------------------------------------------
// AUTH ENDPOINT – return JWT token
// ------------------------------------------------------
app.MapPost("/auth/token", ([FromBody] LoginRequest login) =>
{
    if (login.Username != "demo" || login.Password != "P@ssword123")
        return Results.Unauthorized();

    var claims = new[]
    {
        new Claim(ClaimTypes.Name, login.Username),
        new Claim("role", "developer")
    };

    var creds = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

    var token = new JwtSecurityToken(
        claims: claims,
        expires: DateTime.UtcNow.AddHours(1),
        signingCredentials: creds);

    var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

    return Results.Ok(new { access_token = tokenString });
});

// ------------------------------------------------------
// PROTECTED ENDPOINTS
// ------------------------------------------------------
app.MapGet("/api/v1/orders", async (OrderDbContext db) =>
    Results.Ok(await db.Orders.ToListAsync()))
.RequireAuthorization();

app.MapPost("/api/v1/orders", async (
    [FromBody] CreateOrderRequest request,
    OrderDbContext db) =>
{
    if (request?.Items == null || !request.Items.Any())
        return Results.BadRequest("Order must have at least one item.");

    var order = new Order
    {
        Id = Guid.NewGuid().ToString("n"),
        UserId = request.UserId,
        CreatedAt = DateTimeOffset.UtcNow,
        Status = "Pending",
        Items = request.Items.Select(i => new OrderItem
        {
            ProductId = i.ProductId,
            Quantity = i.Quantity,
            UnitPrice = i.UnitPrice
        }).ToList()
    };

    db.Orders.Add(order);
    await db.SaveChangesAsync();

    var total = order.Items.Sum(i => i.Quantity * i.UnitPrice);

    var correlationId = Guid.NewGuid().ToString("N");

    var evt = new OrderCreatedEvent(
        order.Id,
        order.UserId,
        total,
        order.Items.Select(i => new OrderItemEvent(i.ProductId, i.Quantity)).ToList()
    );

    var json = JsonSerializer.Serialize(evt);

    var pubsubMessage = new PubsubMessage
    {
        Data = ByteString.CopyFromUtf8(json),
        Attributes =
        {
            ["eventType"] = "order.created",
            ["correlationId"] = correlationId
        }
    };

    await orderPublisher.PublishAsync(pubsubMessage);

    Console.WriteLine($"📤 Published order.created order={order.Id} total={total} corr={correlationId}");

    return Results.Created($"/api/v1/orders/{order.Id}", order);
})
.RequireAuthorization();

// ------------------------------------------------------
// Public endpoints (for k8s / Prometheus)
// ------------------------------------------------------
app.MapGet("/healthz/live", () => Results.Ok("Alive"));
app.MapGet("/healthz/ready", () => Results.Ok("Ready"));

app.Run();

// ======================================================
// Subscriber: receives stock.reserved / stock.reservation_failed
// and updates Order status
// ======================================================
public class StockEventsSubscriber : BackgroundService
{
    private readonly ILogger<StockEventsSubscriber> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SubscriberClient _subscriber;

    public StockEventsSubscriber(
        ILogger<StockEventsSubscriber> logger,
        IServiceScopeFactory scopeFactory,
        SubscriberClient subscriber)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _subscriber = subscriber;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("✅ Order StockEventsSubscriber started.");

        await _subscriber.StartAsync(async (msg, ct) =>
        {
            var eventType = msg.Attributes.TryGetValue("eventType", out var t) ? t : "unknown";
            var correlationId = msg.Attributes.TryGetValue("correlationId", out var c) ? c : "none";

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();

                var json = msg.Data.ToStringUtf8();

                if (eventType == "stock.reserved")
                {
                    var e = JsonSerializer.Deserialize<StockReservedEvent>(json)!;
                    var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == e.OrderId, ct);
                    if (order != null) order.Status = "Confirmed";
                }
                else if (eventType == "stock.reservation_failed")
                {
                    var e = JsonSerializer.Deserialize<StockReservationFailedEvent>(json)!;
                    var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == e.OrderId, ct);
                    if (order != null) order.Status = "Rejected";
                }
                else
                {
                    _logger.LogInformation("Ignoring eventType={EventType} corr={CorrelationId}", eventType, correlationId);
                    return SubscriberClient.Reply.Ack;
                }

                await db.SaveChangesAsync(ct);

                _logger.LogInformation("✅ Order updated from {EventType} corr={CorrelationId}", eventType, correlationId);
                return SubscriberClient.Reply.Ack;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed processing {EventType} corr={CorrelationId}", eventType, correlationId);
                return SubscriberClient.Reply.Nack;
            }
        });
    }
}

// ------------------------------------------------------
// Request DTOs
// ------------------------------------------------------
public record LoginRequest(string Username, string Password);

public record CreateOrderRequest(
    string UserId,
    List<OrderItemDto> Items
);

public record OrderItemDto(
    string ProductId,
    int Quantity,
    decimal UnitPrice
);

// ------------------------------------------------------
// Event contracts
// ------------------------------------------------------
public record OrderCreatedEvent(
    string OrderId,
    string UserId,
    decimal Total,
    List<OrderItemEvent> Items
);

public record OrderItemEvent(string ProductId, int Quantity);

public record StockReservedEvent(string OrderId);

public record StockReservationFailedEvent(string OrderId, string Reason);
