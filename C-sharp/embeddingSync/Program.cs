using Pgvector;
using Pgvector.Npgsql;
using Npgsql;
using DotNetEnv;
using EmbeddingSync;
using EmbeddingSync.Services;

#pragma warning disable 618
NpgsqlConnection.GlobalTypeMapper.UseVector();
#pragma warning restore 618

var builder = WebApplication.CreateBuilder(args);

// Load .env from current directory
Env.Load();

// Minimal services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register embedding provider + sync service
builder.Services.AddHttpClient<IEmbeddingProvider, OllamaEmbeddingProvider>((sp, http) =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var baseUrl = cfg["APP__EMBED__BASEADDRESS"] ?? cfg["APP__EMBED__ENDPOINT"] ?? "http://localhost:11434/";
    http.BaseAddress = new Uri(baseUrl);
});
builder.Services.AddScoped<EmbeddingSyncService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/health", () => Results.Ok(new { ok = true }));

// Backfill endpoints
app.MapPost("/api/backfill/products", async (EmbeddingSyncService svc, CancellationToken ct) =>
{
    var n = await svc.BackfillProductsAsync(ct);
    return Results.Ok(new { upserts = n });
});

app.MapPost("/api/backfill/suppliers", async (EmbeddingSyncService svc, CancellationToken ct) =>
{
    var n = await svc.BackfillSuppliersAsync(ct);
    return Results.Ok(new { upserts = n });
});

app.MapPost("/api/backfill/categories", async (EmbeddingSyncService svc, CancellationToken ct) =>
{
    var n = await svc.BackfillCategoriesAsync(ct);
    return Results.Ok(new { upserts = n });
});

// Query endpoints
app.MapPost("/api/query/products", async (EmbeddingSyncService svc, EmbeddingSync.QueryReq req, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Input))
        return Results.BadRequest(new { error = "input required" });
    var results = await svc.QueryProductsAsync(req.Input!, 10, ct);
    return Results.Ok(new { results });
});

app.MapPost("/api/query/suppliers", async (EmbeddingSyncService svc, EmbeddingSync.QueryReq req, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Input))
        return Results.BadRequest(new { error = "input required" });
    var results = await svc.QuerySuppliersAsync(req.Input!, 10, ct);
    return Results.Ok(new { results });
});

app.MapPost("/api/query/categories", async (EmbeddingSyncService svc, EmbeddingSync.QueryReq req, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Input))
        return Results.BadRequest(new { error = "input required" });
    var results = await svc.QueryCategoriesAsync(req.Input!, 10, ct);
    return Results.Ok(new { results });
});

app.Run();
