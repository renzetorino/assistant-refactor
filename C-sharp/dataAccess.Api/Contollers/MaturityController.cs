// ============================================================================
// COMMENTED OUT: Business Maturity Controller
// ============================================================================
// REASON: Business Maturity feature did not meet team and advisor standards.
// ISSUES IDENTIFIED:
//   1. Data Quality: Scores based on placeholder logic and hardcoded values
//   2. Architecture: Pollutes ai_insights table (data conflict with mentorship)
//   3. Reliability: Small datasets return default 50 scores (not decision-ready)
//   4. Incomplete: Missing ActivityLog, purchase_orders, and forecast tracking
// PLAN: Will be refactored with proper metrics collection in future sprint
// DATE COMMENTED: January 15, 2026
// ============================================================================

/*
using dataAccess.Api.Contracts;
using dataAccess.Api.Middleware;
using dataAccess.Planning.Maturity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace dataAccess.Api.Controllers
{
    /// <summary>
    /// Endpoints for business maturity analytics and learning progress.
    /// </summary>
    [ApiController]
    [Route("api/maturity")]
    [Authorize]
    public class MaturityController : ControllerBase
    {
        private readonly MaturityReportGenerator _reportGenerator;
        private readonly ILogger<MaturityController> _logger;

        public MaturityController(
            MaturityReportGenerator reportGenerator,
            ILogger<MaturityController> logger)
        {
            _reportGenerator = reportGenerator;
            _logger = logger;
        }

        /// <summary>
        /// GET /api/maturity/report
        /// Returns a comprehensive maturity report with 3-month trends and AI coaching.
        /// </summary>
        [HttpGet("report")]
        [ProducesResponseType(typeof(MaturityReportDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetMaturityReport(CancellationToken ct)
        {
            try
            {
                // Extract business_id from JWT claims (set by auth middleware)
                if (!TryResolveBusinessId(out var businessId, out var failureResult))
                {
                    return failureResult!;
                }

                _logger.LogInformation("[MaturityController] Generating maturity report | BusinessId: {BusinessId}", businessId);

                var report = await _reportGenerator.GenerateReportAsync(businessId, ct);

                if (!report.Success)
                {
                    _logger.LogError("[MaturityController] Report generation failed | BusinessId: {BusinessId}, Error: {Error}",
                        businessId, report.ErrorMessage);
                    return StatusCode(500, new { error = report.ErrorMessage });
                }

                // Map to DTO
                var dto = new MaturityReportDto
                {
                    BusinessId = report.BusinessId,
                    CurrentScores = new MaturityScoresDto
                    {
                        AgilityScore = report.CurrentScores.AgilityScore,
                        DisciplineScore = report.CurrentScores.DisciplineScore,
                        TrustScore = report.CurrentScores.TrustScore,
                        HealthScore = report.CurrentScores.HealthScore,
                        Metrics = new MetricsDto
                        {
                            AvgResponseTimeHours = report.CurrentScores.Metrics.AvgResponseTimeHours,
                            AvgBookkeepingLagDays = report.CurrentScores.Metrics.AvgBookkeepingLagDays,
                            ForecastAdherencePercent = report.CurrentScores.Metrics.ForecastAdherencePercent,
                            StockoutAlertCount = report.CurrentScores.Metrics.StockoutAlertCount,
                            ExpenseCount = report.CurrentScores.Metrics.ExpenseCount,
                            ForecastComparisonCount = report.CurrentScores.Metrics.ForecastComparisonCount
                        }
                    },
                    MonthlyTrends = report.MonthlyTrends.Select(t => new MonthlyTrendDto
                    {
                        Year = t.Year,
                        Month = t.Month,
                        AgilityScore = t.AgilityScore,
                        DisciplineScore = t.DisciplineScore,
                        TrustScore = t.TrustScore,
                        HealthScore = t.HealthScore
                    }).ToList(),
                    MaturityLevel = report.MaturityLevel,
                    GeneratedAt = report.GeneratedAt
                };

                _logger.LogInformation(
                    "[MaturityController] Report delivered | BusinessId: {BusinessId}, HealthScore: {HealthScore}",
                    businessId, report.CurrentScores.HealthScore);

                return Ok(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[MaturityController] Unexpected error generating maturity report");
                return StatusCode(500, new { error = "An unexpected error occurred" });
            }
        }

        /// <summary>
        /// Extracts business_id from HttpContext (set by middleware via JWT custom claim).
        /// </summary>
        private bool TryResolveBusinessId(out int businessId, out ActionResult? failureResult)
        {
            businessId = 0;
            failureResult = null;

            // Extract from middleware context
            if (HttpContext.Items.ContainsKey("BusinessId") 
                && HttpContext.Items["BusinessId"] is int extractedId)
            {
                businessId = extractedId;
                return true;
            }

            // Fallback: check JWT claims directly
            var businessIdClaim = User?.FindFirst("business_id");
            if (businessIdClaim != null && int.TryParse(businessIdClaim.Value, out businessId))
            {
                _logger.LogWarning(
                    "[MaturityController] BusinessId found in claims but not in middleware context | BusinessId: {BusinessId}",
                    businessId);
                return true;
            }

            _logger.LogError("[MaturityController] Missing business_id in JWT claims");

            failureResult = StatusCode(403, new { error = "Business context is required. Ensure your JWT contains the business_id custom claim." });

            return false;
        }
    }
}
*/
