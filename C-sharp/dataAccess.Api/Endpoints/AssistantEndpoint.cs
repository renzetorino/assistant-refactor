using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Text.Json.Nodes;
using dataAccess.Reports;

namespace dataAccess.Api.Endpoints;

/// Unified assistant endpoint: routes to YAML report runner now; keep /api/nlq for quick answers.
public static class AssistantEndpoint
{
    public static IEndpointConventionBuilder MapAssistant(this IEndpointRouteBuilder app)
        => app.MapPost("/api/assistant", Handle);

    public sealed record AskRequest(string Text, string Domain); // Domain: "expense" | "sales" | "inventory"
    public sealed record AskResponse(string Mode, string? Markdown, object? UiSpec);

    public static async Task<IResult> Handle(
        HttpContext ctx,
        YamlReportRunner yamlRunner,
        CancellationToken ct)
    {
        try
        {
            // Try to read as JSON node first to handle flexible property names
            var jsonNode = await ctx.Request.ReadFromJsonAsync<JsonNode>(cancellationToken: ct);
            if (jsonNode is null)
                return Results.BadRequest("Request body is required.");

            // Extract text (handle both "text" and "Text")
            var text = jsonNode["text"]?.GetValue<string>() 
                       ?? jsonNode["Text"]?.GetValue<string>() 
                       ?? string.Empty;

            // Extract domain (handle both "domain" and "Domain", default to empty)
            var domain = jsonNode["domain"]?.GetValue<string>() 
                        ?? jsonNode["Domain"]?.GetValue<string>() 
                        ?? string.Empty;

            if (string.IsNullOrWhiteSpace(text))
                return Results.BadRequest(new { error = "Text field is required." });

            // Domain is optional now - will be inferred by router if not provided
            var domainToUse = string.IsNullOrWhiteSpace(domain) ? "sales" : domain.ToLowerInvariant();

            // Run the report
            var ui = await yamlRunner.RunAsync(domainToUse, text, ct);
            return Results.Json(new AskResponse("report", null, ui));
        }
        catch (Exception ex)
        {
            var errorId = Guid.NewGuid();
            // Log to console for now (should use ILogger in production)
            Console.WriteLine($"[AssistantEndpoint] ERROR {errorId}: {ex}");
            
            return Results.Json(
                new { 
                    mode = "error",
                    error = $"An error occurred while processing your request. Error ID: {errorId}",
                    details = ex.Message 
                },
                statusCode: 500
            );
        }
    }
}
