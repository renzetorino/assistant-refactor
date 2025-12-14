namespace dataAccess.Services;

/// <summary>
/// Interface for JSON-based FAQ service with semantic search.
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
}
