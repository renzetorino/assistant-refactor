using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using dataAccess.Services;
using System.Diagnostics;

namespace dataAccess.Api.Controllers;

/// <summary>
/// Health check endpoint for monitoring service status
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly AppDbContext _businessContext;
    private readonly AiDbContext _aiContext;
    private readonly ILogger<HealthController> _logger;
    private readonly IConfiguration _configuration;

    public HealthController(
        AppDbContext businessContext,
        AiDbContext aiContext,
        ILogger<HealthController> logger,
        IConfiguration configuration)
    {
        _businessContext = businessContext;
        _aiContext = aiContext;
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Basic health check - returns 200 OK if service is running
    /// </summary>
    /// <returns>Service status</returns>
    [HttpGet]
    [ProducesResponseType(200)]
    public IActionResult Get()
    {
        return Ok(new
        {
            status = "healthy",
            service = "BuiswAIz API",
            timestamp = DateTime.UtcNow,
            version = "1.0.0"
        });
    }

    /// <summary>
    /// Detailed health check - validates all dependencies
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Detailed health status of all components</returns>
    [HttpGet("detailed")]
    [ProducesResponseType(typeof(DetailedHealthResponse), 200)]
    [ProducesResponseType(typeof(DetailedHealthResponse), 503)]
    public async Task<ActionResult<DetailedHealthResponse>> GetDetailed(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var checks = new List<HealthCheck>();

        // 1. Check Database Connection
        var dbCheck = await CheckDatabaseAsync(cancellationToken);
        checks.Add(dbCheck);

        // 2. Check Plugin Files
        var pluginCheck = await CheckPluginFilesAsync();
        checks.Add(pluginCheck);

        // 3. Check LLM API Configuration
        var llmCheck = CheckLlmConfiguration();
        checks.Add(llmCheck);

        // 4. Check Session Cleanup (expired sessions count)
        var sessionCheck = await CheckSessionsAsync(cancellationToken);
        checks.Add(sessionCheck);

        stopwatch.Stop();

        var allHealthy = checks.All(c => c.Status == "healthy");
        var response = new DetailedHealthResponse
        {
            Status = allHealthy ? "healthy" : "degraded",
            Service = "BuiswAIz API",
            Timestamp = DateTime.UtcNow,
            Version = "1.0.0",
            CheckDurationMs = (int)stopwatch.ElapsedMilliseconds,
            Checks = checks
        };

        _logger.LogInformation(
            "Health check completed in {Duration}ms. Status: {Status}",
            stopwatch.ElapsedMilliseconds,
            response.Status
        );

        return allHealthy ? Ok(response) : StatusCode(503, response);
    }

    private async Task<HealthCheck> CheckDatabaseAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            // Check both business and AI databases
            var businessCanConnect = await _businessContext.Database.CanConnectAsync(ct);
            var aiCanConnect = await _aiContext.Database.CanConnectAsync(ct);
            sw.Stop();

            if (!businessCanConnect || !aiCanConnect)
            {
                return new HealthCheck
                {
                    Component = "Database",
                    Status = "unhealthy",
                    ResponseTimeMs = (int)sw.ElapsedMilliseconds,
                    Message = $"Database connection issues: Business DB: {businessCanConnect}, AI DB: {aiCanConnect}"
                };
            }

            // Check if migrations are up to date for both
            var businessPendingMigrations = await _businessContext.Database.GetPendingMigrationsAsync(ct);
            var aiPendingMigrations = await _aiContext.Database.GetPendingMigrationsAsync(ct);
            var hasPendingMigrations = businessPendingMigrations.Any() || aiPendingMigrations.Any();

            return new HealthCheck
            {
                Component = "Database",
                Status = hasPendingMigrations ? "degraded" : "healthy",
                ResponseTimeMs = (int)sw.ElapsedMilliseconds,
                Message = hasPendingMigrations 
                    ? $"Databases connected, but have pending migrations (Business: {businessPendingMigrations.Count()}, AI: {aiPendingMigrations.Count()})" 
                    : "Both databases connected and migrations up to date",
                Details = new Dictionary<string, object>
                {
                    ["business_db"] = _businessContext.Database.GetConnectionString()?.Split(';').FirstOrDefault() ?? "N/A",
                    ["ai_db"] = _aiContext.Database.GetConnectionString()?.Split(';').FirstOrDefault() ?? "N/A",
                    ["business_pending_migrations"] = businessPendingMigrations.Count(),
                    ["ai_pending_migrations"] = aiPendingMigrations.Count()
                }
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Database health check failed");
            return new HealthCheck
            {
                Component = "Database",
                Status = "unhealthy",
                ResponseTimeMs = (int)sw.ElapsedMilliseconds,
                Message = $"Database connection failed: {ex.Message}"
            };
        }
    }

    private async Task<HealthCheck> CheckPluginFilesAsync()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var pluginsPath = Path.Combine(Directory.GetCurrentDirectory(), "Plugins");
            var requiredPlugins = new[]
            {
                "Orchestration/Router",
                "Database/GenerateSql",
                "Analysis/SummarizeResults",
                "BusinessRules/RetrievePolicy"
            };

            var missingPlugins = new List<string>();
            var invalidPlugins = new List<string>();

            foreach (var plugin in requiredPlugins)
            {
                var pluginPath = Path.Combine(pluginsPath, plugin);
                var promptFile = Path.Combine(pluginPath, "skprompt.txt");
                var configFile = Path.Combine(pluginPath, "config.json");

                if (!Directory.Exists(pluginPath))
                {
                    missingPlugins.Add($"{plugin} (directory missing)");
                    continue;
                }

                if (!System.IO.File.Exists(promptFile))
                {
                    invalidPlugins.Add($"{plugin} (skprompt.txt missing)");
                }

                if (!System.IO.File.Exists(configFile))
                {
                    invalidPlugins.Add($"{plugin} (config.json missing)");
                }
            }

            sw.Stop();

            var allValid = !missingPlugins.Any() && !invalidPlugins.Any();
            return new HealthCheck
            {
                Component = "Plugin Files",
                Status = allValid ? "healthy" : "unhealthy",
                ResponseTimeMs = (int)sw.ElapsedMilliseconds,
                Message = allValid 
                    ? $"All {requiredPlugins.Length} required plugins are valid" 
                    : $"Plugin validation failed: {missingPlugins.Count + invalidPlugins.Count} issues found",
                Details = new Dictionary<string, object>
                {
                    ["required_plugins"] = requiredPlugins.Length,
                    ["missing_plugins"] = missingPlugins,
                    ["invalid_plugins"] = invalidPlugins
                }
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Plugin files health check failed");
            return new HealthCheck
            {
                Component = "Plugin Files",
                Status = "unhealthy",
                ResponseTimeMs = (int)sw.ElapsedMilliseconds,
                Message = $"Plugin validation failed: {ex.Message}"
            };
        }
    }

    private HealthCheck CheckLlmConfiguration()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var groqApiKey = _configuration["Groq:ApiKey"] ?? _configuration["GROQ_API_KEY"];
            var hasGroqKey = !string.IsNullOrWhiteSpace(groqApiKey);

            sw.Stop();

            return new HealthCheck
            {
                Component = "LLM Configuration",
                Status = hasGroqKey ? "healthy" : "unhealthy",
                ResponseTimeMs = (int)sw.ElapsedMilliseconds,
                Message = hasGroqKey 
                    ? "LLM API keys configured" 
                    : "Missing GROQ_API_KEY configuration",
                Details = new Dictionary<string, object>
                {
                    ["groq_api_key_configured"] = hasGroqKey,
                    ["fast_llm_model"] = _configuration["Groq:FastModel"] ?? "llama-3.1-8b-instant",
                    ["smart_llm_model"] = _configuration["Groq:SmartModel"] ?? "llama-3.3-70b-versatile"
                }
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "LLM configuration health check failed");
            return new HealthCheck
            {
                Component = "LLM Configuration",
                Status = "unhealthy",
                ResponseTimeMs = (int)sw.ElapsedMilliseconds,
                Message = $"Configuration check failed: {ex.Message}"
            };
        }
    }

    private async Task<HealthCheck> CheckSessionsAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var now = DateTime.UtcNow;
            var expiredCount = await _aiContext.ChatSessions
                .Where(s => s.ExpiresAt < now)
                .CountAsync(ct);

            var activeCount = await _aiContext.ChatSessions
                .Where(s => s.ExpiresAt >= now)
                .CountAsync(ct);

            sw.Stop();

            var needsCleanup = expiredCount > 100;

            return new HealthCheck
            {
                Component = "Session Management",
                Status = needsCleanup ? "degraded" : "healthy",
                ResponseTimeMs = (int)sw.ElapsedMilliseconds,
                Message = needsCleanup 
                    ? $"{expiredCount} expired sessions need cleanup" 
                    : $"Session management healthy ({activeCount} active, {expiredCount} expired)",
                Details = new Dictionary<string, object>
                {
                    ["active_sessions"] = activeCount,
                    ["expired_sessions"] = expiredCount,
                    ["needs_cleanup"] = needsCleanup
                }
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Session management health check failed");
            return new HealthCheck
            {
                Component = "Session Management",
                Status = "unhealthy",
                ResponseTimeMs = (int)sw.ElapsedMilliseconds,
                Message = $"Session check failed: {ex.Message}"
            };
        }
    }
}

/// <summary>
/// Detailed health response with all component checks
/// </summary>
public class DetailedHealthResponse
{
    public string Status { get; set; } = string.Empty;
    public string Service { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string Version { get; set; } = string.Empty;
    public int CheckDurationMs { get; set; }
    public List<HealthCheck> Checks { get; set; } = new();
}

/// <summary>
/// Individual health check result
/// </summary>
public class HealthCheck
{
    public string Component { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // "healthy", "degraded", "unhealthy"
    public int ResponseTimeMs { get; set; }
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, object>? Details { get; set; }
}
