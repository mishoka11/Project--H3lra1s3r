using H3lRa1s3r.Api.DesignService.Infra;
using Google.Cloud.PubSub.V1;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Prometheus;
using Serilog;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Design_Service.DesignService;

var builder = WebApplication.CreateBuilder(args);

// ------------------------------------------------------
// Logging
// ------------------------------------------------------
builder.Host.UseSerilog((ctx, cfg) =>
    cfg.ReadFrom.Configuration(ctx.Configuration).WriteTo.Console());

// ------------------------------------------------------
// JWT Authentication
// ------------------------------------------------------
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new Exception("Jwt:Key missing in DesignService appsettings.json");

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
// Database
// ------------------------------------------------------
var user = Environment.GetEnvironmentVariable("POSTGRES_USER");
var pass = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD");

var conn =
    $"Host=postgres-design.h3llra1s3r.svc.cluster.local;Port=5432;Database=designdb;Username={user};Password={pass};Pooling=true;";

builder.Services.AddDbContext<DesignDbContext>(o => o.UseNpgsql(conn));
builder.Services.AddHealthChecks().AddNpgSql(conn);

// Pub/Sub listener
builder.Services.AddHostedService<DesignPubSubService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// ------------------------------------------------------
// Middleware
// ------------------------------------------------------
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseMetricServer();
app.UseHttpMetrics();

// ------------------------------------------------------
// AUTH – generate JWT token
// ------------------------------------------------------
app.MapPost("/auth/token", () =>
{
    var claims = new[] { new Claim(ClaimTypes.Name, "design-user") };
    var creds = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

    var token = new JwtSecurityToken(
        claims: claims,
        expires: DateTime.UtcNow.AddHours(1),
        signingCredentials: creds);

    return Results.Ok(new
    {
        access_token = new JwtSecurityTokenHandler().WriteToken(token)
    });
});

// ------------------------------------------------------
// PROTECTED ENDPOINTS
// ------------------------------------------------------
app.MapPost("/api/v1/designs", async (Design d, DesignDbContext db) =>
{
    d.Id = string.IsNullOrWhiteSpace(d.Id) ? Guid.NewGuid().ToString("n") : d.Id;
    db.Designs.Add(d);
    await db.SaveChangesAsync();
    return Results.Created($"/api/v1/designs/{d.Id}", d);
})
.RequireAuthorization();

app.MapGet("/api/v1/designs/{id}", async (string id, DesignDbContext db) =>
{
    var d = await db.Designs.FindAsync(id);
    return d is not null ? Results.Ok(d) : Results.NotFound();
})
.RequireAuthorization();

app.MapGet("/api/v1/designs", async (DesignDbContext db) =>
    Results.Ok(await db.Designs.ToListAsync()))
    .RequireAuthorization();

// ------------------------------------------------------
// Public health + readiness endpoints (K8s need this open)
// ------------------------------------------------------
app.MapHealthChecks("/healthz/live");
app.MapHealthChecks("/healthz/ready");

app.Run();

// ------------------------------------------------------
// Pub/Sub Listener (MUST remain public / not behind JWT)
// ------------------------------------------------------
public class DesignPubSubService : BackgroundService
{
    private readonly ILogger<DesignPubSubService> _logger;
    private readonly string _subscriptionName;

    public DesignPubSubService(IConfiguration config, ILogger<DesignPubSubService> logger)
    {
        _logger = logger;
        var projectId = config["Gcp:ProjectId"] ?? "linear-pointer-479410-n7";
        _subscriptionName = $"projects/{projectId}/subscriptions/design-orders-sub";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscription = SubscriptionName.Parse(_subscriptionName);
        var subscriber = await SubscriberClient.CreateAsync(subscription);

        await subscriber.StartAsync(async (msg, ct) =>
        {
            try
            {
                var json = msg.Data.ToStringUtf8();
                var dto = JsonSerializer.Deserialize<OrderCreatedDto>(json);

                _logger.LogInformation("Design received OrderCreated: {OrderId}", dto?.OrderId);
                return SubscriberClient.Reply.Ack;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Design PubSub error");
                return SubscriberClient.Reply.Nack;
            }
        });
    }
}

public record OrderCreatedDto(string OrderId, string UserId, decimal Total);
