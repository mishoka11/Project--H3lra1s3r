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

var conn = $"Host=postgres-catalog.h3llra1s3r.svc.cluster.local;Port=5432;Database=catalogdb;Username={user};Password={pass};Pooling=true;";

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

builder.Services.AddHostedService<PubSubBackgroundService>();

var app = builder.Build();

// ------------------------------------------------------
// Migrate + Seed
// ------------------------------------------------------
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    db.Database.Migrate();
    Seed.AddDemoProducts(db);
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
        var sub = SubscriptionName.Parse(_subscription);
        var client = await SubscriberClient.CreateAsync(sub);

        await client.StartAsync(async (msg, _) =>
        {
            var json = msg.Data.ToStringUtf8();
            var dto = JsonSerializer.Deserialize<OrderCreatedDto>(json);

            _logger.LogInformation("Catalog received OrderCreated: {OrderId}", dto?.OrderId);

            return SubscriberClient.Reply.Ack;
        });
    }
}

public record OrderCreatedDto(string OrderId, string UserId, decimal Total);
