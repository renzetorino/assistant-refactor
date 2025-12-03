using dataAccess.Contracts;
using dataAccess.Planning;

namespace dataAccess.Reports;

/// <summary>
/// Interface for YAML-driven report generation with slot-filling validation.
/// </summary>
public interface IYamlReportRunner
{
    /// <summary>
    /// Executes a report based on a validated planner result.
    /// Performs slot validation against YAML rules before execution.
    /// Returns OrchestrationStepResult with clarification request if slots are missing.
    /// </summary>
    /// <param name="intent">The intent string (e.g., "reports.sales", "reports.inventory", "reports.expenses")</param>
    /// <param name="plannerResult">The validated planner result with slots</param>
    /// <param name="ct">Cancellation token</param>
    Task<OrchestrationStepResult> RunReportAsync(string intent, PlannerResult plannerResult, CancellationToken ct = default);
}
