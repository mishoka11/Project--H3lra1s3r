using Google.Cloud.PubSub.V1;
using H3lRa1s3r.Api.CatalogService;
using H3lRa1s3r.Api.CatalogService.Infra;
using H3lRa1s3r.Api.CatalogService.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Prometheus;
using Serilog;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Linq;

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

// ---- PubSub background worker is now OPTIONAL ----
var enablePubSub = builder.Configuration.GetValue<bool>("PubSub:Enabled", false);
if (enablePubSub)
{
    builder.Services.AddHostedService<PubSubBackgroundService>();
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
        // Create schema if it doesn't exist
        db.Database.EnsureCreated();

        // Seed only when empty
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
        // Log but DO NOT crash the host
        startupLogger.LogError(ex, "Error while initializing/seeding the catalog database.");
    }
}

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

// ------------------------------------------------------
// PubSub Listener
// ------------------------------------------------------
public class PubSubBackgroundService : BackgroundService
{
    private readonly ILogger<PubSubBackgroundService> _logger;
    private readonly string _subscription;

    public PubSubBackgroundService(IConfiguration cfg, ILogger<PubSubBackgroundService> logger)
    {
        _logger = logger;
        var project = cfg["Gcp:ProjectId"] ?? "linear-pointer-479410-n7";
        _subscription = $"projects/{project}/subscriptions/catalog-orders-sub";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var sub = SubscriptionName.Parse(_subscription);
            var client = await SubscriberClient.CreateAsync(sub);

            await client.StartAsync(async (msg, _) =>
            {
                var json = msg.Data.ToStringUtf8();
                var dto = JsonSerializer.Deserialize<OrderCreatedDto>(json);

                _logger.LogInformation(
                    "Catalog received OrderCreated: {OrderId}",
                    dto?.OrderId
                );

                return SubscriberClient.Reply.Ack;
            });
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            // In environments without Pub/Sub permissions (like your GKE cluster),
            // we log the error but DO NOT crash the host.
            _logger.LogError(ex, "PubSub listener failed; continuing without PubSub.");
        }
    }
}

public record OrderCreatedDto(string OrderId, string UserId, decimal Total);
