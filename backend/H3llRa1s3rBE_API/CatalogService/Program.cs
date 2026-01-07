using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using H3lRa1s3r.Api.CatalogService;
using H3lRa1s3r.Api.CatalogService.Infra;
using H3lRa1s3r.Api.CatalogService.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Prometheus;
using Serilog;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// ------------------------------------------------------
// Logging
// ------------------------------------------------------
builder.Host.UseSerilog((ctx, cfg) =>
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .WriteTo.Console());

// ------------------------------------------------------
// JWT
// ------------------------------------------------------
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new Exception("Missing Jwt:Key");

var signingKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
    System.Text.Encoding.UTF8.GetBytes(jwtKey)
);


builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
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
// Database
// ------------------------------------------------------
var user = Environment.GetEnvironmentVariable("POSTGRES_USER");
var pass = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD");

var conn =
    $"Host=postgres-catalog.h3llra1s3r.svc.cluster.local;Port=5432;Database=catalogdb;Username={user};Password={pass};Pooling=true;";

builder.Services.AddDbContext<CatalogDbContext>(o => o.UseNpgsql(conn));
builder.Services.AddHealthChecks().AddNpgSql(conn);

// ------------------------------------------------------
// Other services
// ------------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddResponseCaching();
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

// ------------------------------------------------------
// Pub/Sub SAGA worker (enabled by default)
// ------------------------------------------------------
var enablePubSub = builder.Configuration.GetValue<bool>("PubSub:Enabled", true);
if (enablePubSub)
{
    builder.Services.AddHostedService<CatalogOrdersSubscriber>();
}

var app = builder.Build();

// ------------------------------------------------------
// Ensure DB + Seed (safe, non-fatal)
// ------------------------------------------------------
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
    var startupLogger = loggerFactory.CreateLogger("CatalogStartup");

    try
    {
        db.Database.EnsureCreated();

        if (!db.Products.Any())
        {
            Seed.AddDemoProducts(db);
            startupLogger.LogInformation("Catalog database ensured and seeded with demo products.");
        }
        else
        {
            startupLogger.LogInformation("Catalog database already contains products; skipping seeding.");
        }
    }
    catch (Exception ex)
    {
        startupLogger.LogError(ex, "Error while initializing/seeding the catalog database.");
    }
}

// ------------------------------------------------------
// Middleware
// ------------------------------------------------------
app.UseSwagger();
app.UseSwaggerUI();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseResponseCaching();

app.UseMetricServer();
app.UseHttpMetrics();

// ------------------------------------------------------
// Protected endpoints
// ------------------------------------------------------
app.MapGet("/api/v1/catalog", async (CatalogDbContext db) =>
        Results.Ok(await db.Products.ToListAsync()))
    .RequireAuthorization();

app.MapGet("/api/v1/catalog/{id}", async (string id, CatalogDbContext db) =>
{
    var p = await db.Products.FindAsync(id);
    return p is not null ? Results.Ok(p) : Results.NotFound();
})
.RequireAuthorization();

// ------------------------------------------------------
// Health endpoints (public)
// ------------------------------------------------------
app.MapHealthChecks("/healthz/live");
app.MapHealthChecks("/healthz/ready");

app.Run();

// ======================================================
// Pub/Sub Background Worker: Catalog consumes order.created
// and publishes stock.reserved / stock.reservation_failed
// ======================================================
public class CatalogOrdersSubscriber : BackgroundService
{
    private readonly ILogger<CatalogOrdersSubscriber> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    private readonly string _projectId;
    private readonly string _ordersSubscriptionId;
    private readonly string _stockTopicId;

    public CatalogOrdersSubscriber(
        IConfiguration cfg,
        ILogger<CatalogOrdersSubscriber> logger,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;

        _projectId = cfg["Gcp:ProjectId"] ?? "linear-pointer-479410-n7";
        _ordersSubscriptionId = cfg["Gcp:CatalogOrdersSubscription"] ?? "catalog-orders-sub";
        _stockTopicId = cfg["Gcp:StockTopic"] ?? "stock-events";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var subName = SubscriptionName.FromProjectSubscription(_projectId, _ordersSubscriptionId);
            var subscriber = await SubscriberClient.CreateAsync(subName);

            var stockTopicName = TopicName.FromProjectTopic(_projectId, _stockTopicId);
            var publisher = await PublisherClient.CreateAsync(stockTopicName);

            _logger.LogInformation("✅ Catalog SAGA worker started. Sub={Sub} PublishTopic={Topic}", subName, stockTopicName);

            await subscriber.StartAsync(async (msg, ct) =>
            {
                var eventType = msg.Attributes.TryGetValue("eventType", out var t) ? t : "unknown";
                var correlationId = msg.Attributes.TryGetValue("correlationId", out var c) ? c : Guid.NewGuid().ToString("N");

                if (eventType != "order.created")
                {
                    _logger.LogInformation("Ignoring message eventType={EventType}", eventType);
                    return SubscriberClient.Reply.Ack;
                }

                var json = msg.Data.ToStringUtf8();
                var orderCreated = JsonSerializer.Deserialize<OrderCreatedEvent>(json);

                if (orderCreated?.Items == null || orderCreated.Items.Count == 0)
                {
                    _logger.LogWarning("Invalid order.created payload corr={CorrelationId}", correlationId);
                    return SubscriberClient.Reply.Ack;
                }

                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

                    // 1) Validate stock
                    foreach (var item in orderCreated.Items)
                    {
                        var p = await db.Products.FindAsync(new object[] { item.ProductId }, ct);
                        if (p == null || p.Stock < item.Quantity)
                        {
                            await PublishStockFailed(publisher, orderCreated.OrderId, "OUT_OF_STOCK", correlationId);
                            _logger.LogWarning("❌ Stock reservation failed order={OrderId} corr={CorrelationId}", orderCreated.OrderId, correlationId);
                            return SubscriberClient.Reply.Ack;
                        }
                    }

                    // 2) Reserve stock (decrement)
                    foreach (var item in orderCreated.Items)
                    {
                        var p = await db.Products.FindAsync(new object[] { item.ProductId }, ct);
                        p!.Stock -= item.Quantity;
                    }

                    await db.SaveChangesAsync(ct);

                    await PublishStockReserved(publisher, orderCreated.OrderId, correlationId);
                    _logger.LogInformation("✅ Stock reserved order={OrderId} corr={CorrelationId}", orderCreated.OrderId, correlationId);

                    return SubscriberClient.Reply.Ack;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing order.created order={OrderId} corr={CorrelationId}", orderCreated.OrderId, correlationId);
                    return SubscriberClient.Reply.Nack;
                }
            });
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "PubSub listener failed; continuing without PubSub.");
        }
    }

    private static Task PublishStockReserved(PublisherClient publisher, string orderId, string correlationId)
    {
        var evt = new StockReservedEvent(orderId);
        var json = JsonSerializer.Serialize(evt);

        var message = new PubsubMessage
        {
            Data = ByteString.CopyFromUtf8(json),
            Attributes =
            {
                ["eventType"] = "stock.reserved",
                ["correlationId"] = correlationId
            }
        };

        return publisher.PublishAsync(message);
    }

    private static Task PublishStockFailed(PublisherClient publisher, string orderId, string reason, string correlationId)
    {
        var evt = new StockReservationFailedEvent(orderId, reason);
        var json = JsonSerializer.Serialize(evt);

        var message = new PubsubMessage
        {
            Data = ByteString.CopyFromUtf8(json),
            Attributes =
            {
                ["eventType"] = "stock.reservation_failed",
                ["correlationId"] = correlationId
            }
        };

        return publisher.PublishAsync(message);
    }
}

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
