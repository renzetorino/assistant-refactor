using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using dataAccess.Api.Contracts;
using dataAccess.Planning.Insights;
using System.Security.Claims;
using System.Text.Json;

namespace dataAccess.Api.Controllers;

/// <summary>
/// Business Mentor AI Insights API - provides intelligent business coaching based on real-time data analysis.
/// </summary>
[ApiController]
[Authorize(Policy = "ApiUser")]
[Route("api/mentor-insights")]
[Produces("application/json")]
[EnableRateLimiting("mentor-insights")]
public class MentorInsightsController : ControllerBase
{
    private readonly InsightCoordinator _coordinator;
    private readonly ILogger<MentorInsightsController> _logger;
    private readonly IServiceScopeFactory _serviceScopeFactory;

    public MentorInsightsController(
        InsightCoordinator coordinator,
        ILogger<MentorInsightsController> logger,
        IServiceScopeFactory serviceScopeFactory)
    {
        _coordinator = coordinator;
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;
    }

    /// <summary>
    /// Gets the latest mentor insights for the authenticated business.
    /// Returns cached insights if fresh (within 30 minutes) and data unchanged.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Latest insights grouped by category (inventory, finance, sales)</returns>
    /// <remarks>
    /// Sample request:
    /// 
    ///     GET /api/mentor-insights/latest
    ///     Authorization: Bearer {jwt_token}
    /// 
    /// Sample response:
    /// 
    ///     {
    ///       "insights": [
    ///         {
    ///           "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    ///           "category": "inventory",
    ///           "mentorMessage": "Magandang balita! Pero may ilang items na malapit nang maubos...",
    ///           "detectorVersion": "v1.0.0",
    ///           "createdAt": "2026-01-04T10:30:00Z",
    ///           "sourceEvent": "scheduled",
    ///           "findingCount": 3
    ///         }
    ///       ],
    ///       "cacheHit": true,
    ///       "cacheAgeMinutes": 15,
    ///       "executionTimeMs": 45,
    ///       "detectorVersion": "v1.0.0"
    ///     }
    /// 
    /// </remarks>
    /// <response code="200">Returns the latest insights</response>
    /// <response code="401">If authentication fails</response>
    /// <response code="403">If business_id claim is missing from JWT</response>
    /// <response code="500">If an internal error occurs</response>
    [HttpGet("latest")]
    [ProducesResponseType(typeof(InsightsOperationResponse), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 401)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    [ProducesResponseType(typeof(ProblemDetails), 500)]
    public async Task<ActionResult<InsightsOperationResponse>> GetLatestAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            // SECURITY: Extract business_id from JWT claims (set by BusinessScopingMiddleware)
            if (!TryResolveBusinessId(out var businessId, out var authFailure))
            {
                return authFailure!;
            }

            _logger.LogInformation(
                "[MentorInsights] GetLatest | BusinessId: {BusinessId}, UserId: {UserId}",
                businessId,
                GetUserId());

            // Get insights through coordinator (uses cache when appropriate)
            var result = await _coordinator.GetLatestInsightsAsync(
                businessId,
                sourceEvent: "api-latest",
                forceRefresh: false,
                ct: cancellationToken);

            if (!result.Success)
            {
                _logger.LogError(
                    "[MentorInsights] Failed to get insights | BusinessId: {BusinessId}, Error: {Error}",
                    businessId,
                    result.ErrorMessage);

                return StatusCode(500, new ProblemDetails
                {
                    Status = 500,
                    Title = "Insight Generation Failed",
                    Detail = result.ErrorMessage ?? "An unexpected error occurred while generating insights."
                });
            }

            // Map to DTO
            var response = new InsightsOperationResponse
            {
                Insights = result.Insights.Select(MapToDto).ToList(),
                CacheHit = result.CacheHit,
                CacheAgeMinutes = result.CacheHit ? result.CacheAgeMinutes : null,
                ExecutionTimeMs = result.TotalDurationMs,
                DetectorVersion = result.DetectorVersion,
                Message = result.CacheHit 
                    ? $"Using cached insights from {result.CacheAgeMinutes} minutes ago" 
                    : "Fresh insights generated"
            };

            _logger.LogInformation(
                "[MentorInsights] Success | BusinessId: {BusinessId}, Count: {Count}, CacheHit: {CacheHit}, Duration: {Duration}ms",
                businessId,
                response.Insights.Count,
                result.CacheHit,
                result.TotalDurationMs);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[MentorInsights] Unexpected error | BusinessId: {BusinessId}",
                TryResolveBusinessId(out var bid, out _) ? bid : -1);

            return StatusCode(500, new ProblemDetails
            {
                Status = 500,
                Title = "Internal Server Error",
                Detail = "An unexpected error occurred. Please try again later."
            });
        }
    }

    /// <summary>
    /// Forces a fresh scan and regenerates all insights, bypassing cache.
    /// Use this when you want to see updated insights immediately after making business changes.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Freshly generated insights</returns>
    /// <remarks>
    /// Sample request:
    /// 
    ///     POST /api/mentor-insights/refresh
    ///     Authorization: Bearer {jwt_token}
    /// 
    /// This endpoint:
    /// - Runs all detectors against current business data
    /// - Formats findings using Gemini AI
    /// - Persists new insights to database
    /// - Returns fresh results immediately
    /// 
    /// Rate limited to 10 requests per minute per business to prevent API abuse.
    /// 
    /// </remarks>
    /// <response code="200">Returns freshly generated insights</response>
    /// <response code="401">If authentication fails</response>
    /// <response code="403">If business_id claim is missing from JWT</response>
    /// <response code="429">If rate limit exceeded (10 req/min)</response>
    /// <response code="500">If an internal error occurs</response>
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(InsightsOperationResponse), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 401)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    [ProducesResponseType(typeof(ProblemDetails), 429)]
    [ProducesResponseType(typeof(ProblemDetails), 500)]
    public async Task<ActionResult<InsightsOperationResponse>> RefreshAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            // SECURITY: Extract business_id from JWT claims
            if (!TryResolveBusinessId(out var businessId, out var authFailure))
            {
                return authFailure!;
            }

            _logger.LogInformation(
                "[MentorInsights] Refresh (force) | BusinessId: {BusinessId}, UserId: {UserId}",
                businessId,
                GetUserId());

            // Force fresh scan, bypassing cache
            var result = await _coordinator.GetLatestInsightsAsync(
                businessId,
                sourceEvent: "api-refresh",
                forceRefresh: true,
                ct: cancellationToken);

            if (!result.Success)
            {
                _logger.LogError(
                    "[MentorInsights] Refresh failed | BusinessId: {BusinessId}, Error: {Error}",
                    businessId,
                    result.ErrorMessage);

                return StatusCode(500, new ProblemDetails
                {
                    Status = 500,
                    Title = "Insight Refresh Failed",
                    Detail = result.ErrorMessage ?? "An unexpected error occurred while refreshing insights."
                });
            }

            // Map to DTO
            var response = new InsightsOperationResponse
            {
                Insights = result.Insights.Select(MapToDto).ToList(),
                CacheHit = false, // Always false for refresh
                ExecutionTimeMs = result.TotalDurationMs,
                DetectorVersion = result.DetectorVersion,
                Message = $"Fresh insights generated from live data scan ({result.ScanDurationMs}ms scan)"
            };

            _logger.LogInformation(
                "[MentorInsights] Refresh success | BusinessId: {BusinessId}, Count: {Count}, ScanDuration: {ScanDuration}ms, TotalDuration: {TotalDuration}ms",
                businessId,
                response.Insights.Count,
                result.ScanDurationMs,
                result.TotalDurationMs);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[MentorInsights] Refresh unexpected error | BusinessId: {BusinessId}",
                TryResolveBusinessId(out var bid, out _) ? bid : -1);

            return StatusCode(500, new ProblemDetails
            {
                Status = 500,
                Title = "Internal Server Error",
                Detail = "An unexpected error occurred. Please try again later."
            });
        }
    }

    #region Private Helpers

    /// <summary>
    /// Extracts business_id from HttpContext (set by BusinessScopingMiddleware via JWT custom claim).
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
                "[MentorInsights] BusinessId found in claims but not in middleware context | BusinessId: {BusinessId}",
                businessId);
            return true;
        }

        _logger.LogError(
            "[MentorInsights] Missing business_id | UserId: {UserId} | JWT must contain business_id custom claim",
            GetUserId());

        failureResult = StatusCode(403, new ProblemDetails
        {
            Status = 403,
            Title = "Forbidden",
            Detail = "Business context is required. Ensure your JWT contains the business_id custom claim."
        });

        return false;
    }

    /// <summary>
    /// Gets the user ID from JWT claims for logging purposes.
    /// </summary>
    private string GetUserId()
    {
        var claim = User?.FindFirst(ClaimTypes.NameIdentifier)
                    ?? User?.FindFirst("sub")
                    ?? User?.FindFirst("user_id");

        return claim?.Value ?? "unknown";
    }

    /// <summary>
    /// Maps AiInsight entity to DTO with finding count extraction.
    /// </summary>
    private InsightResponseDto MapToDto(Entities.AiInsight insight)
    {
        var findingCount = 0;

        // Try to parse raw_findings to count findings
        try
        {
            if (!string.IsNullOrWhiteSpace(insight.RawFindings))
            {
                using var doc = JsonDocument.Parse(insight.RawFindings);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    findingCount = doc.RootElement.GetArrayLength();
                }
            }
        }
        catch
        {
            // Ignore parsing errors, just leave count as 0
        }

        return new InsightResponseDto
        {
            Id = insight.Id,
            Category = insight.Category,
            MentorMessage = insight.AiSummary ?? string.Empty,
            DetectorVersion = insight.DetectorVersion,
            CreatedAt = insight.CreatedAt,
            SourceEvent = insight.SourceEvent ?? "unknown",
            FindingCount = findingCount
        };
    }

    #endregion

    #region Specialized Insight Endpoints

    /// <summary>
    /// Gets Morning Briefing insight - top 3 business priorities for the day.
    /// </summary>
    /// <response code="200">Returns the morning briefing text</response>
    /// <response code="401">If authentication fails</response>
    /// <response code="403">If business_id claim is missing</response>
    [HttpGet("morning-briefing")]
    [ProducesResponseType(typeof(SpecializedInsightResponse), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 401)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    public async Task<ActionResult<SpecializedInsightResponse>> GetMorningBriefingAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            if (!TryResolveBusinessId(out var businessId, out var authFailure))
            {
                return authFailure!;
            }

            _logger.LogInformation(
                "[MentorInsights] GetMorningBriefing | BusinessId: {BusinessId}",
                businessId);

            var briefing = await _coordinator.GetMorningBriefingAsync(businessId, cancellationToken);

            return Ok(new SpecializedInsightResponse
            {
                InsightType = "morning-briefing",
                Content = briefing,
                GeneratedAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MentorInsights] GetMorningBriefing failed");
            return StatusCode(500, new ProblemDetails
            {
                Title = "Failed to generate morning briefing",
                Detail = ex.Message,
                Status = 500
            });
        }
    }

    /// <summary>
    /// Gets Inventory Risk insight for a specific product.
    /// </summary>
    /// <param name="productId">The product ID to analyze</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <response code="200">Returns the inventory risk analysis</response>
    /// <response code="401">If authentication fails</response>
    /// <response code="403">If business_id claim is missing</response>
    [HttpGet("inventory-risk/{productId}")]
    [ProducesResponseType(typeof(SpecializedInsightResponse), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 401)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    public async Task<ActionResult<SpecializedInsightResponse>> GetInventoryRiskAsync(
        [FromRoute] int productId,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!TryResolveBusinessId(out var businessId, out var authFailure))
            {
                return authFailure!;
            }

            _logger.LogInformation(
                "[MentorInsights] GetInventoryRisk | BusinessId: {BusinessId}, ProductId: {ProductId}",
                businessId, productId);

            var analysis = await _coordinator.GetInventoryRiskAsync(businessId, productId, cancellationToken);

            return Ok(new SpecializedInsightResponse
            {
                InsightType = "inventory-risk",
                Content = analysis,
                GeneratedAt = DateTime.UtcNow,
                Metadata = new Dictionary<string, object>
                {
                    ["productId"] = productId
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MentorInsights] GetInventoryRisk failed");
            return StatusCode(500, new ProblemDetails
            {
                Title = "Failed to generate inventory risk analysis",
                Detail = ex.Message,
                Status = 500
            });
        }
    }

    /// <summary>
    /// Gets Profit Teaching insight - explains revenue vs profit margins.
    /// </summary>
    /// <param name="periodDays">Analysis period in days (default: 90)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <response code="200">Returns the profit teaching content</response>
    /// <response code="401">If authentication fails</response>
    /// <response code="403">If business_id claim is missing</response>
    [HttpGet("profit-teaching")]
    [ProducesResponseType(typeof(SpecializedInsightResponse), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 401)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    public async Task<ActionResult<SpecializedInsightResponse>> GetProfitTeachingAsync(
        [FromQuery] int periodDays = 90,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!TryResolveBusinessId(out var businessId, out var authFailure))
            {
                return authFailure!;
            }

            _logger.LogInformation(
                "[MentorInsights] GetProfitTeaching | BusinessId: {BusinessId}, PeriodDays: {PeriodDays}",
                businessId, periodDays);

            var teaching = await _coordinator.GetProfitTeachingAsync(businessId, periodDays, cancellationToken);

            return Ok(new SpecializedInsightResponse
            {
                InsightType = "profit-teaching",
                Content = teaching,
                GeneratedAt = DateTime.UtcNow,
                Metadata = new Dictionary<string, object>
                {
                    ["periodDays"] = periodDays
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MentorInsights] GetProfitTeaching failed");
            return StatusCode(500, new ProblemDetails
            {
                Title = "Failed to generate profit teaching",
                Detail = ex.Message,
                Status = 500
            });
        }
    }

    /// <summary>
    /// Gets Expense Forecast insight - analyzes spending trends and seasonality.
    /// </summary>
    /// <param name="targetMonth">Target month to analyze (ISO 8601 format). Defaults to current month.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <response code="200">Returns the expense forecast</response>
    /// <response code="401">If authentication fails</response>
    /// <response code="403">If business_id claim is missing</response>
    [HttpGet("expense-forecast")]
    [ProducesResponseType(typeof(SpecializedInsightResponse), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 401)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    public async Task<ActionResult<SpecializedInsightResponse>> GetExpenseForecastAsync(
        [FromQuery] string? targetMonth = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!TryResolveBusinessId(out var businessId, out var authFailure))
            {
                return authFailure!;
            }

            // Parse targetMonth if provided (format: YYYY-MM-DD or YYYY-MM)
            DateTime? parsedMonth = null;
            if (!string.IsNullOrWhiteSpace(targetMonth) && DateTime.TryParse(targetMonth, out var dt))
            {
                parsedMonth = dt;
            }

            _logger.LogInformation(
                "[MentorInsights] GetExpenseForecast | BusinessId: {BusinessId}, TargetMonth: {TargetMonth}",
                businessId, parsedMonth);

            var forecast = await _coordinator.GetExpenseForecastAsync(businessId, parsedMonth, cancellationToken);

            return Ok(new SpecializedInsightResponse
            {
                InsightType = "expense-forecast",
                Content = forecast,
                GeneratedAt = DateTime.UtcNow,
                Metadata = new Dictionary<string, object>
                {
                    ["targetMonth"] = parsedMonth?.ToString("yyyy-MM") ?? DateTime.UtcNow.ToString("yyyy-MM")
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MentorInsights] GetExpenseForecast failed");
            return StatusCode(500, new ProblemDetails
            {
                Title = "Failed to generate expense forecast",
                Detail = ex.Message,
                Status = 500
            });
        }
    }

    #endregion

    #region Background Refresh (Sprint 6)

    /// <summary>
    /// Triggers a background refresh of all insights for the authenticated business.
    /// Used on login to pre-populate cache without blocking user experience.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>202 Accepted - job queued in background</returns>
    /// <remarks>
    /// Sample request:
    /// 
    ///     POST /api/mentor/insights/refresh
    ///     Authorization: Bearer {jwt_token}
    /// 
    /// Sample response:
    /// 
    ///     {
    ///       "message": "Insights refresh queued",
    ///       "businessId": 42
    ///     }
    /// 
    /// </remarks>
    /// <response code="202">Refresh job accepted and queued</response>
    /// <response code="401">If authentication fails</response>
    /// <response code="403">If business_id claim is missing</response>
    [HttpPost("refresh-async")]
    [ProducesResponseType(typeof(RefreshResponse), 202)]
    [ProducesResponseType(typeof(ProblemDetails), 401)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    public async Task<IActionResult> RefreshInsightsAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            if (!TryResolveBusinessId(out var businessId, out var authFailure))
            {
                return authFailure!;
            }

            _logger.LogInformation(
                "[MentorInsights] RefreshInsights triggered | BusinessId: {BusinessId}, UserId: {UserId}",
                businessId,
                GetUserId());

            // Trigger background refresh without blocking response
            // Create new scope to avoid ObjectDisposedException with EF Core DbContext
            _ = Task.Run(async () =>
            {
                using (var scope = _serviceScopeFactory.CreateScope())
                {
                    try
                    {
                        var coordinator = scope.ServiceProvider.GetRequiredService<InsightCoordinator>();
                        await coordinator.GetLatestInsightsAsync(
                            businessId,
                            sourceEvent: "login_refresh",
                            forceRefresh: false, // Let coordinator decide based on cache
                            ct: CancellationToken.None); // Don't cancel background job

                        _logger.LogInformation(
                            "[MentorInsights] Background refresh completed | BusinessId: {BusinessId}",
                            businessId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "[MentorInsights] Background refresh failed | BusinessId: {BusinessId}",
                            businessId);
                    }
                }
            });

            // Return immediately (non-blocking)
            return Accepted(new RefreshResponse
            {
                Message = "Insights refresh queued successfully",
                BusinessId = businessId,
                Status = "processing"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MentorInsights] RefreshInsights endpoint failed");
            return StatusCode(500, new ProblemDetails
            {
                Title = "Failed to queue refresh",
                Detail = ex.Message,
                Status = 500
            });
        }
    }

    #endregion
}

/// <summary>
/// Response for specialized insight endpoints.
/// </summary>
public class SpecializedInsightResponse
{
    public string InsightType { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Response for the background refresh endpoint (Sprint 6).
/// </summary>
public class RefreshResponse
{
    public string Message { get; set; } = string.Empty;
    public int BusinessId { get; set; }
    public string Status { get; set; } = "queued";
}
