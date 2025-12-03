using System.Text.Json;
using dataAccess.Api.Contracts.Common;

namespace dataAccess.Api.Contracts;

/// <summary>
/// Standard return type for all orchestration runners (YamlReportRunner, YamlForecastRunner, etc.)
/// Supports both execution results and slot-filling clarification flow.
/// </summary>
public sealed class OrchestrationStepResult
{
    /// <summary>
    /// Indicates whether the operation completed successfully without errors.
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// Error message if IsSuccess is false.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// The actual report data if this was a successful report generation.
    /// Null if not a report or if RequiresClarification is true.
    /// </summary>
    public ReportResult? ReportData { get; set; }

    /// <summary>
    /// Indicates that the system needs user clarification before proceeding.
    /// When true, the assistant should ask the ClarificationPrompt and wait for user response.
    /// </summary>
    public bool RequiresClarification { get; set; }

    /// <summary>
    /// Name of the missing parameter/slot (e.g., "sub_intent", "date_range", "forecast_days").
    /// Only populated when RequiresClarification is true.
    /// </summary>
    public string? MissingParameterName { get; set; }

    /// <summary>
    /// The clarification question to present to the user (from YAML configuration).
    /// Only populated when RequiresClarification is true.
    /// </summary>
    public string? ClarificationPrompt { get; set; }

    /// <summary>
    /// The pending plan that should be stored in session state for later resumption.
    /// Only populated when RequiresClarification is true.
    /// Will be deserialized and used when the user provides the missing information.
    /// </summary>
    public JsonDocument? PendingPlan { get; set; }
}

/// <summary>
/// Placeholder for ReportResult. Will be replaced with actual report result type.
/// </summary>
public sealed class ReportResult
{
    public Guid? Id { get; set; }
    public string? Title { get; set; }
    public string? PeriodLabel { get; set; }
    public JsonDocument? UiSpec { get; set; }
}
