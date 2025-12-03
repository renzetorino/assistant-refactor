using System.Text.Json;
using dataAccess.LLM;
using dataAccess.Planning;
using dataAccess.Reports;
using Microsoft.Extensions.Logging;

namespace dataAccess.Services;

/// <summary>
/// LLM-based date parser service that uses Groq's 8B model to convert natural language
/// date queries into precise DateTime ranges. This replaces the brittle static DateRangeResolver.
/// </summary>
public sealed class LlmDateParser : ILlmDateParser
{
    private readonly GroqJsonClient _groq;
    private readonly PromptLoader _promptLoader;
    private readonly ILogger<LlmDateParser> _logger;
    private readonly string _promptTemplate;

    public LlmDateParser(
        GroqJsonClient groq,
        PromptLoader promptLoader,
        ILogger<LlmDateParser> logger)
    {
        _groq = groq;
        _promptLoader = promptLoader;
        _logger = logger;

        // Load the datetime parsing prompt template
        _promptTemplate = _promptLoader.ReadText("datetime.parse.yaml");
    }

    public async Task<(DateTime StartDate, DateTime EndDate)> ParseDateRangeAsync(
        string dateQuery,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dateQuery))
        {
            _logger.LogWarning("Empty date query provided, defaulting to 'yesterday'");
            dateQuery = "yesterday";
        }

        try
        {
            _logger.LogInformation("Parsing date query with LLM: {DateQuery}", dateQuery);

            // Prepare the prompt with current date
            var currentDate = DateTime.Now.ToString("yyyy-MM-dd");
            var systemPrompt = _promptTemplate
                .Replace("[CURRENT_DATE]", currentDate);

            // Call Groq LLM (8B specialist model - NO HISTORY)
            // IMPORTANT: Explicitly pass data: null to use the correct overload (without history)
            var result = await _groq.CompleteJsonAsyncChat(
                system: systemPrompt,
                user: $"Parse this date query: \"{dateQuery}\"",
                data: null,  // 🔧 FIX: Explicitly pass null to avoid history overload
                temperature: 0.0,
                ct: ct);

            // Parse the JSON response
            var json = result.RootElement;
            
            if (!json.TryGetProperty("startDate", out var startDateElem) ||
                !json.TryGetProperty("endDate", out var endDateElem))
            {
                throw new InvalidOperationException(
                    $"LLM response missing required fields. Response: {result.RootElement}");
            }

            // Check for NULL values from LLM (meaning no date was found in user query)
            if (startDateElem.ValueKind == JsonValueKind.Null || endDateElem.ValueKind == JsonValueKind.Null)
            {
                _logger.LogInformation(
                    "LLM returned null dates (no date found in query '{Query}'). Triggering clarification flow.",
                    dateQuery);
                
                // Throw exception to signal NO DATE FOUND → triggers clarification
                throw new InvalidOperationException("No date found in query - clarification required");
            }

            var startDateStr = startDateElem.GetString();
            var endDateStr = endDateElem.GetString();

            // Validate ISO format (yyyy-MM-dd) - reject natural language strings
            if (string.IsNullOrWhiteSpace(startDateStr) || string.IsNullOrWhiteSpace(endDateStr))
            {
                throw new InvalidOperationException(
                    $"LLM returned empty date strings. Response: {result.RootElement}");
            }

            // Strict parsing - only accept valid date formats
            if (!DateTime.TryParseExact(startDateStr, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var startDate) ||
                !DateTime.TryParseExact(endDateStr, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var endDate))
            {
                throw new InvalidOperationException(
                    $"Invalid date format from LLM (expected yyyy-MM-dd). Start: {startDateStr}, End: {endDateStr}");
            }

            _logger.LogInformation(
                "Successfully parsed date query '{Query}' to range: {Start} to {End}",
                dateQuery, startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"));

            return (startDate, endDate);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse date query with LLM: {DateQuery}", dateQuery);
            
            // Re-throw to signal orchestrator that slot filling failed
            // This will trigger clarification flow instead of using bad default dates
            throw;
        }
    }
}
