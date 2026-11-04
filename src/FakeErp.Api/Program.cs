using System.Globalization;
using FakeErp.Api;

// ERP fictício para rodar o worker localmente.
// - Gera pedidos em memória e altera alguns a cada poucos segundos.
// - Falha de propósito em parte das chamadas (503) para exercitar o retry do worker.
// - De vez em quando envia o status "EM_ANALISE", que não tem de-para no worker.

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<OrderStore>();
builder.Services.AddHostedService<OrderMutator>();

var app = builder.Build();

var failureRate = app.Configuration.GetValue("Chaos:FailureRate", 0.1);

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api") && Random.Shared.NextDouble() < failureRate)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsync("ERP indisponível (falha simulada).");
        return;
    }

    await next();
});

app.MapGet("/api/orders", (OrderStore store, string? modifiedSince, int page = 1, int pageSize = 50) =>
{
    var since = DateTime.MinValue;

    if (!string.IsNullOrWhiteSpace(modifiedSince) &&
        !DateTime.TryParse(modifiedSince, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out since))
    {
        return Results.BadRequest("modifiedSince inválido.");
    }

    page = Math.Max(page, 1);
    pageSize = Math.Clamp(pageSize, 1, 200);

    return Results.Ok(store.Query(since, page, pageSize));
});

app.MapGet("/api/orders/{id}", (OrderStore store, string id) =>
    store.Find(id) is { } order ? Results.Ok(order) : Results.NotFound());

app.MapGet("/", () => "ERP fictício no ar. Use GET /api/orders?modifiedSince=...&page=1&pageSize=50");

app.Run();
