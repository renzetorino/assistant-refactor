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
        var req = await ctx.Request.ReadFromJsonAsync<AskRequest>(cancellationToken: ct);
        if (req is null || string.IsNullOrWhiteSpace(req.Text) || string.IsNullOrWhiteSpace(req.Domain))
            return Results.BadRequest("Text and Domain are required.");

        // For now we target report flow directly (router can set Domain upstream)
        var ui = await yamlRunner.RunAsync(req.Domain.ToLowerInvariant(), req.Text, ct);
        return Results.Json(new AskResponse("report", null, ui));
    }
}
