using DesignService.Infra;
using Microsoft.EntityFrameworkCore;
using Prometheus;
using Serilog;
using static H3lRa1s3r.Api.DesignService.Models;

var builder = WebApplication.CreateBuilder(args);

// Logging
builder.Host.UseSerilog((ctx, cfg) =>
    cfg.ReadFrom.Configuration(ctx.Configuration).WriteTo.Console());

// --- Build secure connection string from ENV vars ---
var host = Environment.GetEnvironmentVariable("POSTGRES_HOST")
           ?? "postgres.h3llra1s3r.svc.cluster.local";

var db = Environment.GetEnvironmentVariable("POSTGRES_DB") ?? "h3db";
var user = Environment.GetEnvironmentVariable("POSTGRES_USER");
var pass = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD");

var conn = $"Host={host};Port=5432;Database={db};Username={user};Password={pass};Pooling=true;";

// EF Core
builder.Services.AddDbContext<DesignDbContext>(o => o.UseNpgsql(conn));
builder.Services.AddHealthChecks().AddNpgSql(conn);

// Misc services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
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

// Prometheus
app.UseMetricServer();
app.UseHttpMetrics();

// API Endpoints
app.MapPost("/api/v1/designs", async (Design d, DesignDbContext dbCtx) =>
{
    d.Id ??= Guid.NewGuid().ToString("n");
    d.CreatedAt = DateTimeOffset.UtcNow;
    dbCtx.Designs.Add(d);
    await dbCtx.SaveChangesAsync();
    return Results.Created($"/api/v1/designs/{d.Id}", d);
});

app.MapGet("/api/v1/designs/{id}", async (string id, DesignDbContext dbCtx) =>
{
    var design = await dbCtx.Designs.FindAsync(id);
    return design is not null ? Results.Ok(design) : Results.NotFound();
});

app.MapGet("/api/v1/designs", async (DesignDbContext dbCtx) =>
    Results.Ok(await dbCtx.Designs.ToListAsync()));

app.MapHealthChecks("/healthz/live");
app.MapHealthChecks("/healthz/ready");

app.Run();
