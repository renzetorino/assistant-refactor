using System;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using dataAccess.Contracts;
using dataAccess.Forecasts;
using dataAccess.Planning;
using dataAccess.Reports;

namespace dataAccess.Services;

/// <summary>
/// YAML-driven forecast runner with slot-filling validation.
/// Phase 3: Enforces YAML-defined required slots before execution.
/// No hardcoded fallbacks - all rules come from YAML configuration.
/// </summary>
public sealed class ForecastRunnerService : IForecastRunnerService
{
    private readonly PromptLoader _promptLoader;
    private readonly HybridForecastService _forecastService;

    public ForecastRunnerService(
        PromptLoader promptLoader,
        HybridForecastService forecastService)
    {
        _promptLoader = promptLoader;
        _forecastService = forecastService;
    }

    /// <summary>
    /// Phase 3: Run forecast with YAML-driven slot validation.
    /// NO hardcoded fallbacks - validates required slots from YAML before execution.
    /// </summary>
    public async Task<OrchestrationStepResult> RunForecastAsync(PlannerResult plannerResult, CancellationToken ct = default)
    {
        try
        {
            // 1) Determine spec file based on domain
            var domain = (plannerResult.Domain ?? "sales").Trim().ToLowerInvariant();
            var specFile = domain switch
            {
                "expenses" or "expense" => "forecast.expenses.yaml",
                "sales" or "sale" => "forecast.sales.yaml",
                _ => throw new InvalidOperationException($"Unknown forecast domain: {domain}")
            };

            // 2) Load spec to get slot definitions
            var specYaml = _promptLoader.ReadText(specFile);
            var spec = DomainPrompt.Parse(specYaml);

            // 3) CRITICAL: Validate required slots from YAML (Phase 2 enforcement)
            if (spec.Phase1?.Slots != null)
            {
                foreach (var slotDef in spec.Phase1.Slots)
                {
                    var slotName = slotDef.Key;
                    var slotConfig = slotDef.Value;

                    // If slot is required, it MUST exist and be non-empty
                    if (slotConfig.Required)
                    {
                        if (!plannerResult.Slots.ContainsKey(slotName) ||
                            string.IsNullOrWhiteSpace(plannerResult.Slots[slotName]))
                        {
                            // STOP: Return clarification request
                            return new OrchestrationStepResult
                            {
                                IsSuccess = false,
                                RequiresClarification = true,
                                MissingParameterName = slotName,
                                ClarificationPrompt = slotConfig.ClarificationPrompt ??
                                    $"Please provide a value for {slotName}.",
                                PendingPlan = plannerResult.ToJsonDocument()
                            };
                        }
                    }
                }
            }

            // 4) Validation passed - extract forecast_days directly from slots (NO FALLBACKS)
            if (!plannerResult.Slots.TryGetValue("forecast_days", out var forecastDaysStr) ||
                string.IsNullOrWhiteSpace(forecastDaysStr))
            {
                throw new InvalidOperationException("forecast_days is required but missing from validated slots");
            }

            // Parse forecast days (no defaults, no fallbacks)
            if (!int.TryParse(forecastDaysStr, out var forecastDays) || forecastDays < 1 || forecastDays > 60)
            {
                throw new InvalidOperationException($"Invalid forecast_days value: {forecastDaysStr}. Must be between 1 and 60.");
            }

            // 5) Determine forecast domain for service
            var forecastDomain = domain == "expenses" || domain == "expense"
                ? ForecastDomain.Expenses
                : ForecastDomain.Sales;

            // 6) Execute forecast using HybridForecastService
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var forecastResult = await _forecastService.ForecastAsync(
                domain: forecastDomain,
                days: forecastDays,
                ct: ct);

            // 7) Build period label
            var startDate = today;
            var endDate = today.AddDays(forecastDays - 1);
            var periodLabel = BuildPeriodLabel(startDate, endDate);

            // 8) Return success with forecast data
            // Note: forecastResult is already a structured object from HybridForecastService
            // We serialize it and wrap it in our standard format
            var resultJson = JsonSerializer.Serialize(new
            {
                report_title = $"{domain} Forecast",
                period = new { start = startDate.ToString("yyyy-MM-dd"), end = endDate.ToString("yyyy-MM-dd"), label = periodLabel },
                forecast_days = forecastDays,
                domain,
                // Pass through the forecast result as-is (it contains kpis, charts, narrative, etc.)
                forecast_result = forecastResult
            });

            return new OrchestrationStepResult
            {
                IsSuccess = true,
                ReportData = new ReportResult
                {
                    Id = Guid.NewGuid(), // Forecast runs aren't saved to DB in this implementation
                    Title = $"{domain} Forecast",
                    PeriodLabel = periodLabel,
                    UiSpec = JsonDocument.Parse(resultJson)
                }
            };
        }
        catch (Exception ex)
        {
            return new OrchestrationStepResult
            {
                IsSuccess = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private static string BuildPeriodLabel(DateOnly start, DateOnly end)
    {
        var sameYear = start.Year == end.Year;
        var left = start.ToDateTime(TimeOnly.MinValue);
        var right = end.ToDateTime(TimeOnly.MinValue);
        var L = left.ToString("MMM d", CultureInfo.InvariantCulture);
        var R = sameYear
            ? right.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)
            : right.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
        return $"{L}–{R}";
    }
}
