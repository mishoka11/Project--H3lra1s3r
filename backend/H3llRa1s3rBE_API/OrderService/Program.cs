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
// Pub/Sub Publisher
// ------------------------------------------------------
var projectId = builder.Configuration["Gcp:ProjectId"] ?? "linear-pointer-479410-n7";
var topicId = builder.Configuration["Gcp:OrderTopic"] ?? "orders-topic";
var topicName = TopicName.FromProjectTopic(projectId, topicId);

Console.WriteLine($"🔗 Initializing PubSub publisher for topic: {topicName}");

PublisherClient publisher = await PublisherClient.CreateAsync(topicName);

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

app.UseAuthentication();  // <<< ADD THIS
app.UseAuthorization();   // <<< ADD THIS

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
    if (request == null || request.Items == null || !request.Items.Any())
        return Results.BadRequest("Order must have at least one item.");

    var order = new Order
    {
        Id = Guid.NewGuid().ToString("n"),
        UserId = request.UserId,
        CreatedAt = DateTimeOffset.UtcNow,
        Status = "Created",
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

    var evt = new OrderCreatedDto(order.Id, order.UserId, total);
    string json = JsonSerializer.Serialize(evt);
    var message = ByteString.CopyFromUtf8(json);

    await publisher.PublishAsync(message);

    Console.WriteLine($"📤 Published OrderCreated event: {order.Id} (total={total})");

    return Results.Created($"/api/v1/orders/{order.Id}", order);
})
.RequireAuthorization();

// ------------------------------------------------------
// Public endpoints (for k8s / Prometheus)
// ------------------------------------------------------
app.MapGet("/healthz/live", () => Results.Ok("Alive"));
app.MapGet("/healthz/ready", () => Results.Ok("Ready"));

app.Run();

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

public record OrderCreatedDto(
    string OrderId,
    string UserId,
    decimal Total
);
