using System.Threading;

namespace dataAccess.Services;

/// <summary>
/// Interface for JSON-based FAQ service with semantic search, analytics, and confidence scoring.
/// Enables mocking in unit tests.
/// </summary>
public interface IJsonFaqService
{
    /// <summary>
    /// Search for the most relevant FAQ answer using cosine similarity.
    /// </summary>
    /// <param name="query">User's natural language question</param>
    /// <param name="threshold">Minimum similarity score (0-1). Default: 0.7</param>
    /// <returns>Best matching FAQ answer, or null if no good match</returns>
    Task<string?> SearchAsync(string query, double threshold = 0.7);

    /// <summary>
    /// Search for the most relevant FAQ answer with analytics tracking and confidence scoring.
    /// </summary>
    /// <param name="query">User's natural language question</param>
    /// <param name="userId">User ID for analytics tracking</param>
    /// <param name="threshold">Minimum similarity score (0-1). Default: 0.7</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>FAQ result with answer, confidence score, and question, or null if no match</returns>
    Task<FaqSearchResult?> SearchWithAnalyticsAsync(
        string query, 
        Guid userId, 
        double threshold = 0.7,
        CancellationToken ct = default);

    /// <summary>
    /// Get similar questions when no strong match is found (fallback chain).
    /// </summary>
    /// <param name="query">User's natural language question</param>
    /// <param name="topN">Number of suggestions to return. Default: 3</param>
    /// <param name="minThreshold">Minimum similarity for suggestions. Default: 0.4</param>
    /// <returns>List of similar questions with confidence scores</returns>
    Task<List<FaqSuggestion>> GetSimilarQuestionsAsync(
        string query, 
        int topN = 3, 
        double minThreshold = 0.4);
}
