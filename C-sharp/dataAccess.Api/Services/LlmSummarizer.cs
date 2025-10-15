using dataAccess.LLM;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace dataAccess.Api.Services;

/// <summary>
/// Summarizes SQL query results into human-readable narratives using LLM (8B model).
/// Takes JSON rows and converts them into 2-3 sentence summaries.
/// </summary>
public class LlmSummarizer
{
    private readonly GroqJsonClient _groq;
    private readonly ILogger<LlmSummarizer> _logger;

    public LlmSummarizer(GroqJsonClient groq, ILogger<LlmSummarizer> logger)
    {
        _groq = groq;
        _logger = logger;
    }

    /// <summary>
    /// Generates a human-readable summary from SQL query results.
    /// </summary>
    /// <param name="question">The original user question</param>
    /// <param name="sql">The SQL query that was executed</param>
    /// <param name="results">The JSON array of result rows</param>
    /// <param name="rowCount">The number of rows returned</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A short narrative summary (2-3 sentences)</returns>
    public async Task<string> SummarizeAsync(
        string question,
        string sql,
        JsonElement results,
        int rowCount,
        CancellationToken ct = default)
    {
        try
        {
            var systemPrompt = BuildSystemPrompt();
            var userPrompt = BuildUserPrompt(question, sql, results, rowCount);

            _logger.LogInformation("Generating summary for question: {Question}", question);

            // Use the 8B chat model for fast summarization
            using var doc = await _groq.CompleteJsonAsyncChat(
                systemPrompt, 
                userPrompt, 
                null, 
                0.3,  // Slightly higher temperature for more natural language
                ct);

            if (doc.RootElement.TryGetProperty("summary", out var summaryEl) &&
                summaryEl.ValueKind == JsonValueKind.String)
            {
                var summary = summaryEl.GetString();
                if (!string.IsNullOrWhiteSpace(summary))
                {
                    _logger.LogInformation("Summary generated successfully");
                    return summary;
                }
            }

            _logger.LogWarning("LLM did not return a valid summary");
            return GenerateFallbackSummary(rowCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating summary");
            return GenerateFallbackSummary(rowCount);
        }
    }

    private string BuildSystemPrompt()
    {
        return @"You are a summarizer. Given these rows, explain the results in 2-3 sentences.

Rules:
- Be concise and clear
- Focus on the most important insights
- Use natural, business-friendly language
- Mention specific numbers when relevant
- Do NOT include technical SQL details

Output format (JSON):
{
  ""summary"": ""Your 2-3 sentence summary here""
}";
    }

    private string BuildUserPrompt(string question, string sql, JsonElement results, int rowCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Question: {question}");
        sb.AppendLine($"Row count: {rowCount}");
        sb.AppendLine();
        sb.AppendLine("Results (JSON):");
        
        // Limit the data sent to LLM to avoid token overflow
        var resultsJson = results.GetRawText();
        if (resultsJson.Length > 4000)
        {
            // Truncate if too large
            resultsJson = resultsJson.Substring(0, 4000) + "... (truncated)";
        }
        
        sb.AppendLine(resultsJson);
        
        return sb.ToString();
    }

    private string GenerateFallbackSummary(int rowCount)
    {
        if (rowCount == 0)
            return "No results found for your query.";
        
        if (rowCount == 1)
            return "Found 1 result matching your query.";
        
        return $"Found {rowCount} results matching your query.";
    }
}
