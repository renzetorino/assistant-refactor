namespace dataAccess.Reports;

/// <summary>
/// LLM-based date parser that converts natural language date queries into precise DateTime ranges.
/// Replaces the brittle static DateRangeResolver with a flexible AI-powered parser.
/// </summary>
public interface ILlmDateParser
{
    /// <summary>
    /// Parses a natural language date query (e.g., "last 5 days", "yesterday", "nung isang linggo")
    /// into a precise start and end date range.
    /// </summary>
    /// <param name="dateQuery">Natural language date expression from user</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Tuple containing StartDate and EndDate in DateTime format</returns>
    Task<(DateTime StartDate, DateTime EndDate)> ParseDateRangeAsync(string dateQuery, CancellationToken ct);
}
