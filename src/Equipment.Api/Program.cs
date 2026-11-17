using System.Text.Json.Serialization;
using Equipment.Core;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<Stage>()));

var app = builder.Build();
var simulator = new BatchSimulator(TimeSpan.FromMilliseconds(60));
using var batchSlot = new SemaphoreSlim(1, 1);

app.MapGet("/health", () => Results.Ok(new { status = "ready" }));
app.MapPost("/batches", async (BatchRequest request, CancellationToken cancellationToken) =>
{
    if (BatchSimulator.Validate(request) is string error)
        return Results.BadRequest(new { error });

    if (!await batchSlot.WaitAsync(0, cancellationToken))
        return Results.Conflict(new { error = "A batch is already running. Try again shortly." });

    try
    {
        return Results.Ok(await simulator.RunAsync(request, cancellationToken));
    }
    finally
    {
        batchSlot.Release();
    }
});

await app.RunAsync("http://127.0.0.1:5078");
