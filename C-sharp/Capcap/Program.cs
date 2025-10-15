using Azure;
using Capcap;
using Capcap.Services;
using dataAccess;
using dataAccess.Services;
using DotNetEnv;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using OllamaSharp.Models.Chat;
using Sprache;
using System.Data;
using System.Reflection.PortableExecutable;

var builder = WebApplication.CreateBuilder(args);


// Load .env from the content root (works in VS and `dotnet run`)
DotNetEnv.Env.Load(builder.Environment.ContentRootPath + "/.env");
builder.Configuration.AddEnvironmentVariables();

// Fallback to process env if needed
var conn = builder.Configuration["APP__VEC__CONNECTIONSTRING"]
         ?? Environment.GetEnvironmentVariable("APP__VEC__CONNECTIONSTRING")
         ?? throw new InvalidOperationException("Missing APP__VEC__CONNECTIONSTRING");

Console.WriteLine($"[DB] {conn}");

builder.Services.AddHttpClient<GroqChatLlm>();
builder.Services.AddSingleton<IChatLlm, GroqChatLlm>();
builder.Services.AddDbContext<AppDbContext>(opt => opt.UseNpgsql(conn));

builder.Services.AddScoped<SqlQueryService>();
builder.Services.AddScoped<VectorSearchService>();
builder.Services.AddScoped<HybridQueryService>();

builder.Services.AddSingleton<LightHeuristics>();
builder.Services.AddScoped<QueryClassifier>();
builder.Services.AddScoped<RouterService>();

var app = builder.Build();

app.MapGet("/health", () => "ok");
app.MapGet("/health/db", async (IServiceProvider sp, CancellationToken ct) =>
{
    using var scope = sp.CreateScope();
    var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try { await ctx.Database.ExecuteSqlRawAsync("select 1", ct); return Results.Ok(new { status = "up" }); }
    catch (Exception ex) { return Results.Problem($"DB not reachable: {ex.Message}"); }
});

app.MapGet("/health/groq", async (IChatLlm llm, CancellationToken ct) =>
{
    var reply = await llm.ChatAsync("You are a test system", "Hello from Jose!", ct);
    return Results.Ok(new { reply });
});

var api = app.MapGroup("/api");

api.MapPost("/assistant/chat",
    async (ChatInput req, RouterService router, CancellationToken ct) =>
    {
        string userText =
            (req?.messages is { Count: > 0 })
                ? string.Join("\n", req!.messages!.Select(m => $"{m.role}: {m.content}"))
                : (req?.input ?? string.Empty);

        if (string.IsNullOrWhiteSpace(userText))
            return Results.BadRequest(new { error = "Provide `input` or `messages`." });

        var result = await router.HandleAsync(userText, ct);
        return Results.Ok(result);
    });


api.MapPost("/search/sql",
    async (QueryReq r, SqlQueryService s, CancellationToken ct) =>
        Results.Ok(await s.DispatchAsync(r.input, ct)));

api.MapPost("/search/vector",
    async (QueryReq r, VectorSearchService s, CancellationToken ct) =>
        Results.Ok(await s.DispatchAsync(r.input, ct)));

api.MapPost("/search/hybrid",
    async (QueryReq r, HybridQueryService s, CancellationToken ct) =>
        Results.Ok(await s.DispatchAsync(r.input, ct)));

app.Run();

public sealed record ChatMessage(string role, string content);
public sealed record ChatInput(string? input, List<ChatMessage>? messages);


