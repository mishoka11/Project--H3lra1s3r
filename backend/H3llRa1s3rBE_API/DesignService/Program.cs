using Prometheus;
using Serilog;
using static H3lRa1s3r.Api.DesignService.Models;

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

// ✅ Seed demo design
if (!DesignsDb.Designs.ContainsKey("123"))
{
    var demoDesign = new Design(
        Id: "123",
        UserId: "demo-user",
        Name: "Demo Design",
        JsonPayload: "{\"elements\": [\"circle\", \"square\"]}",
        CreatedAt: DateTimeOffset.UtcNow
    );

    DesignsDb.Designs["123"] = demoDesign;
}

// ---- Endpoints ----
app.MapPost("/api/v1/designs", (Design d) =>
{
    var id = string.IsNullOrWhiteSpace(d.Id) ? Guid.NewGuid().ToString("n") : d.Id;
    DesignsDb.Designs[id] = d with { Id = id, CreatedAt = DateTimeOffset.UtcNow };
    return Results.Created($"/api/v1/designs/{id}", DesignsDb.Designs[id]);
}).WithOpenApi();

app.MapGet("/api/v1/designs/{id}", (string id) =>
    DesignsDb.Designs.TryGetValue(id, out var d)
        ? Results.Ok(d)
        : Results.NotFound()).WithOpenApi();

app.MapHealthChecks("/healthz/live");
app.MapHealthChecks("/healthz/ready");

app.Run();
