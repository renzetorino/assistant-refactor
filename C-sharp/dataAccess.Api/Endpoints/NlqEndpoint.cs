using dataAccess.Planning.Nlq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Text.Json;

namespace dataAccess.Api.Endpoints;

public static class NlqEndpoint
{
    public static IEndpointConventionBuilder MapNlqEndpoint(this IEndpointRouteBuilder app)
        => app.MapPost("/api/nlq", Handle);

    public static async Task<IResult> Handle(HttpContext ctx, NlqService svc, CancellationToken ct)
    {
        // 1) Read body safely (POST required)
        ctx.Request.EnableBuffering();
        string raw;
        using (var r = new StreamReader(ctx.Request.Body, System.Text.Encoding.UTF8, true, 4096, leaveOpen: true))
            raw = await r.ReadToEndAsync();
        ctx.Request.Body.Position = 0;

        using var jdoc = string.IsNullOrWhiteSpace(raw) ? JsonDocument.Parse("{}") : JsonDocument.Parse(raw);
        var root = jdoc.RootElement;
        var text = root.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : "";

        // 2) Run NLQ
        var result = await svc.HandleAsync(text, ct);

        // 3) If it’s a report UI spec, pass it through as JSON (unchanged)
        if (result is JsonElement el) return Results.Json(el, contentType: "application/json");

        // 4) Otherwise it's a chat-style answer.
        //    We support 2 formats for backward compatibility:
        //    A) plain markdown string
        //    B) old shape: { mode:"chat", markdown:"..." }
        if (result is string mdString)
        {
            // Plain markdown already → return as text/markdown
            return Results.Text(mdString, "text/markdown; charset=utf-8");
        }

        // Try to extract "markdown" property if it's an object
        try
        {
            var json = JsonSerializer.Serialize(result);
            using var j = JsonDocument.Parse(json);
            if (j.RootElement.ValueKind == JsonValueKind.Object &&
                j.RootElement.TryGetProperty("markdown", out var mdProp) &&
                mdProp.ValueKind == JsonValueKind.String)
            {
                var md = mdProp.GetString() ?? "";
                return Results.Text(md, "text/markdown; charset=utf-8");
            }
        }
        catch
        {
            // fall-through
        }

        // Fallback: just stringify whatever it is (last resort)
        return Results.Text(result?.ToString() ?? "", "text/markdown; charset=utf-8");
    }
}
