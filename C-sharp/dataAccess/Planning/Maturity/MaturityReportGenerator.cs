// ============================================================================
// COMMENTED OUT: Business Maturity Report Generator
// ============================================================================
// REASON: Did not meet team and advisor standards - data architecture issues
// CRITICAL ISSUES:
//   1. Data Pollution: Writes to ai_insights table with category="maturity"
//      - Conflicts with mentorship insights (morning-briefing, inventory-risk, etc.)
//      - Causes cache lookup confusion between two incompatible systems
//   2. No Caching: Always regenerates reports from scratch (no cache check)
//      - Every request recalculates all 3 scores + monthly trends (~200-300ms)
//   3. Relies on MaturityScoreCalculator with placeholder logic (see above file)
// FUNCTIONALITY:
//   - Generates comprehensive maturity report with 3-month trends
//   - Stores in ai_insights table with JSON serialized findings
//   - Pure mathematical calculations (no AI coaching)
// PLAN: Create separate maturity_reports table or use different insight_type
// DATE COMMENTED: January 15, 2026
// ============================================================================

/*
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using dataAccess.Contracts;
using dataAccess.Entities;
using dataAccess.Services;
using Microsoft.Extensions.Logging;

namespace dataAccess.Planning.Maturity
{
    /// <summary>
    /// Generates comprehensive maturity reports with monthly trends using pure mathematical calculations.
    /// No AI coaching - just the numbers.
    /// </summary>
    public sealed class MaturityReportGenerator
    {
        private readonly MaturityScoreCalculator _calculator;
        private readonly AiDbContext _aiContext;
        private readonly ILogger<MaturityReportGenerator> _logger;

        public MaturityReportGenerator(
            MaturityScoreCalculator calculator,
            AiDbContext aiContext,
            ILogger<MaturityReportGenerator> logger)
        {
            _calculator = calculator;
            _aiContext = aiContext;
            _logger = logger;
        }

        /// <summary>
        /// Generates a complete maturity report with 3-month trends.
        /// </summary>
        public async Task<MaturityReport> GenerateReportAsync(
            int businessId,
            CancellationToken ct = default)
        {
            try
            {
                _logger.LogInformation("[MaturityReportGenerator] Generating report | BusinessId: {BusinessId}", businessId);

                var now = DateTime.UtcNow;
                var report = new MaturityReport
                {
                    BusinessId = businessId,
                    GeneratedAt = now
                };

                // Calculate current period scores (last 30 days)
                var currentScores = await _calculator.CalculateScoresAsync(
                    businessId,
                    now.AddDays(-30),
                    now,
                    ct);

                if (!currentScores.Success)
                {
                    return new MaturityReport
                    {
                        BusinessId = businessId,
                        Success = false,
                        ErrorMessage = currentScores.ErrorMessage
                    };
                }

                report.CurrentScores = currentScores;
                report.MaturityLevel = MaturityScoreCalculator.GetMaturityLevel(currentScores.HealthScore);

                // Calculate monthly trends for last 3 months
                var monthlyTrends = await CalculateMonthlyTrendsAsync(businessId, 3, ct);
                report.MonthlyTrends = monthlyTrends;

                // Persist report to ai_insights table for caching
                await PersistReportAsync(report, ct);

                report.Success = true;

                _logger.LogInformation(
                    "[MaturityReportGenerator] Report generated | BusinessId: {BusinessId}, HealthScore: {HealthScore}, Level: {Level}",
                    businessId, currentScores.HealthScore, report.MaturityLevel);

                return report;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[MaturityReportGenerator] Failed to generate report | BusinessId: {BusinessId}", businessId);
                return new MaturityReport
                {
                    BusinessId = businessId,
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Calculates maturity scores for each of the last N months.
        /// </summary>
        private async Task<List<MonthlyMaturityTrend>> CalculateMonthlyTrendsAsync(
            int businessId,
            int monthCount,
            CancellationToken ct)
        {
            var trends = new List<MonthlyMaturityTrend>();
            var now = DateTime.UtcNow;

            for (int i = monthCount - 1; i >= 0; i--)
            {
                var monthStart = new DateTime(now.Year, now.Month, 1).AddMonths(-i);
                var monthEnd = monthStart.AddMonths(1).AddDays(-1);

                var scores = await _calculator.CalculateScoresAsync(
                    businessId,
                    monthStart,
                    monthEnd,
                    ct);

                if (scores.Success)
                {
                    trends.Add(new MonthlyMaturityTrend
                    {
                        Year = monthStart.Year,
                        Month = monthStart.Month,
                        AgilityScore = scores.AgilityScore,
                        DisciplineScore = scores.DisciplineScore,
                        TrustScore = scores.TrustScore,
                        HealthScore = scores.HealthScore,
                        Metrics = scores.Metrics
                    });
                }
            }

            return trends;
        }

        /// <summary>
        /// Persists maturity report to ai_insights table for caching.
        /// </summary>
        private async Task PersistReportAsync(MaturityReport report, CancellationToken ct)
        {
            try
            {
                var insight = new AiInsight
                {
                    Id = Guid.NewGuid(),
                    BusinessId = report.BusinessId,
                    Category = "maturity",
                    DetectorVersion = "v1.0",
                    RawFindings = JsonSerializer.Serialize(new
                    {
                        currentScores = report.CurrentScores,
                        monthlyTrends = report.MonthlyTrends
                    }),
                    AiSummary = string.Empty, // No AI summary for maturity reports
                    CreatedAt = DateTime.UtcNow,
                    SourceEvent = "maturity_report"
                };

                _aiContext.AiInsights.Add(insight);
                await _aiContext.SaveChangesAsync(ct);

                _logger.LogDebug("[MaturityReportGenerator] Report persisted | BusinessId: {BusinessId}", report.BusinessId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[MaturityReportGenerator] Failed to persist report, continuing anyway");
            }
        }
    }
}
*/
