using dataAccess.Contracts;
using dataAccess.Planning;

namespace dataAccess.Reports;

/// <summary>
/// Interface for YAML-driven forecast generation with slot-filling validation.
/// </summary>
public interface IForecastRunnerService
{
    /// <summary>
    /// Executes a forecast based on a validated planner result.
    /// Performs slot validation against YAML rules before execution.
    /// Returns OrchestrationStepResult with clarification request if slots are missing.
    /// </summary>
    Task<OrchestrationStepResult> RunForecastAsync(PlannerResult plannerResult, CancellationToken ct = default);
}
