using dataAccess.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Text.Json;

namespace dataAccess.Api.Endpoints;

/// <summary>
/// Endpoint for the complete query pipeline:
/// Question -> SQL Generation -> Validation -> Execution -> Summarization -> Response Formatting
/// </summary>
public static class QueryPipelineEndpoint
{
    public static IEndpointConventionBuilder MapQueryPipelineEndpoint(this IEndpointRouteBuilder app)
        => app.MapPost("/api/query", Handle);

    public static async Task<IResult> Handle(
        HttpContext ctx,
        QueryPipeline pipeline,
        CancellationToken ct)
    {
        // Read request body
        ctx.Request.EnableBuffering();
        string raw;
        using (var r = new StreamReader(ctx.Request.Body, System.Text.Encoding.UTF8, true, 4096, leaveOpen: true))
            raw = await r.ReadToEndAsync();
        ctx.Request.Body.Position = 0;

        // Parse JSON
        using var jdoc = string.IsNullOrWhiteSpace(raw) ? JsonDocument.Parse("{}") : JsonDocument.Parse(raw);
        var root = jdoc.RootElement;
        
        // Extract question
        var question = root.TryGetProperty("question", out var q) && q.ValueKind == JsonValueKind.String 
            ? q.GetString() ?? "" 
            : "";

        if (string.IsNullOrWhiteSpace(question))
        {
            return Results.Json(new { success = false, error = "Question is required" }, statusCode: 400);
        }

        // Execute pipeline
        var result = await pipeline.ExecuteAsync(question, ct);

        // Return formatted response
        return Results.Json(result, statusCode: 200);
    }
}
