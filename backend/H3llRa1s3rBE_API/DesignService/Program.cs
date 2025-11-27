using DesignService.Infra;
using Microsoft.EntityFrameworkCore;
using Prometheus;
using Serilog;
using static H3lRa1s3r.Api.DesignService.Models;

var builder = WebApplication.CreateBuilder(args);

// ---- Logging ----
builder.Host.UseSerilog((ctx, cfg) =>
    cfg.ReadFrom.Configuration(ctx.Configuration).WriteTo.Console());

// ---- Services ----
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ---- PostgreSQL + EF Core ----
var user = Environment.GetEnvironmentVariable("POSTGRES_USER");
var pass = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD");

var conn = $"Host=postgres.h3llra1s3r.svc.cluster.local;Port=5432;Database=h3db;Username={user};Password={pass}";

builder.Services.AddDbContext<DesignDbContext>(o => o.UseNpgsql(conn));
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

// Prometheus
app.UseMetricServer();
app.UseHttpMetrics();

// ---- Endpoints ----
app.MapPost("/api/v1/designs", async (Design d, DesignDbContext db) =>
{
    var id = string.IsNullOrWhiteSpace(d.Id) ? Guid.NewGuid().ToString("n") : d.Id;
    var newDesign = new Design
    {
        Id = id,
        UserId = d.UserId,
        Name = d.Name,
        JsonPayload = d.JsonPayload,
        CreatedAt = DateTimeOffset.UtcNow
    };

    db.Designs.Add(newDesign);
    await db.SaveChangesAsync();

    return Results.Created($"/api/v1/designs/{id}", newDesign);
}).WithOpenApi();

app.MapGet("/api/v1/designs/{id}", async (string id, DesignDbContext db) =>
{
    var design = await db.Designs.FindAsync(id);
    return design is not null ? Results.Ok(design) : Results.NotFound();
});

app.MapGet("/api/v1/designs", async (DesignDbContext db) =>
    Results.Ok(await db.Designs.ToListAsync()));

app.MapHealthChecks("/healthz/live");
app.MapHealthChecks("/healthz/ready");

app.Run();
