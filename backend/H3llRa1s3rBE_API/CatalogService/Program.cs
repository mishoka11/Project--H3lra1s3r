using H3lRa1s3r.Api.CatalogService;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Prometheus;
using Serilog;
using System.Threading.RateLimiting;
using static H3lRa1s3r.Api.CatalogService.Models;

var builder = WebApplication.CreateBuilder(args);

// ---- Logging ----
builder.Host.UseSerilog((ctx, cfg) =>
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .Enrich.FromLogContext()
       .WriteTo.Console());

// ---- Services ----
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddResponseCaching();
builder.Services.AddHealthChecks();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .AllowAnyOrigin()
    .AllowAnyHeader()
    .AllowAnyMethod()));

builder.Services.AddRateLimiter(_ => _.GlobalLimiter =
    PartitionedRateLimiter.Create<HttpContext, string>(http =>
        RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "anon",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromSeconds(1),
                QueueLimit = 0
            })));

// ---- Build app ----
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();
app.UseCors();
app.UseRateLimiter();
app.UseResponseCaching();

// 👇 Prometheus middleware here
app.UseMetricServer();
app.UseHttpMetrics();  // <-- this collects http_requests_total and related metrics

// ---- Seed data ----
Seed.AddDemoProducts();

// ---- Endpoints ----
app.MapGet("/api/v1/catalog", () => Results.Ok(Db.Products.Values))
    .WithName("GetCatalog")
    .WithOpenApi();

app.MapGet("/api/v1/catalog/{id}", Results<Ok<Product>, NotFound> (string id) =>
{
    return Db.Products.TryGetValue(id, out var p)
        ? TypedResults.Ok(p)
        : TypedResults.NotFound();
}).WithName("GetProduct");

app.MapHealthChecks("/healthz/live");
app.MapHealthChecks("/healthz/ready");

app.Run();
