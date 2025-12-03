using System.Text.Json;

namespace dataAccess.Planning;

/// <summary>
/// Represents the result from Phase-1 planning with intent, domain, and resolved slots.
/// Used for slot-filling validation and clarification flow.
/// </summary>
public sealed class PlannerResult
{
    /// <summary>
    /// The top-level intent (e.g., "report", "forecast")
    /// </summary>
    public string Intent { get; set; } = string.Empty;

    /// <summary>
    /// The domain (e.g., "sales", "expenses")
    /// </summary>
    public string? Domain { get; set; }

    /// <summary>
    /// The sub-intent/topic (e.g., "Sales", "Inventory", "Expenses")
    /// </summary>
    public string? SubIntent { get; set; }

    /// <summary>
    /// Resolved parameter slots (e.g., "period_start", "period_end", "forecast_days")
    /// </summary>
    public Dictionary<string, string> Slots { get; set; } = new();

    /// <summary>
    /// Confidence score from the planner (0.0 - 1.0)
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Original user text that was planned
    /// </summary>
    public string? UserText { get; set; }

    /// <summary>
    /// Converts this PlannerResult to a JsonDocument for storage
    /// </summary>
    public JsonDocument ToJsonDocument()
    {
        var json = JsonSerializer.Serialize(this);
        return JsonDocument.Parse(json);
    }

    /// <summary>
    /// Creates a PlannerResult from a stored JsonDocument
    /// </summary>
    public static PlannerResult? FromJsonDocument(JsonDocument? doc)
    {
        if (doc == null) return null;
        return JsonSerializer.Deserialize<PlannerResult>(doc.RootElement.GetRawText());
    }
}
