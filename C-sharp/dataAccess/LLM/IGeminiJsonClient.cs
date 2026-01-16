using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace dataAccess.LLM
{
    /// <summary>
    /// Interface for Google Gemini API client that returns structured JSON responses.
    /// Used specifically for Business Mentor AI insights (not general chat).
    /// </summary>
    public interface IGeminiJsonClient
    {
        /// <summary>
        /// Generates a JSON response from Gemini given a system prompt and user message.
        /// </summary>
        /// <param name="systemPrompt">System-level instructions for the LLM (role, constraints, format)</param>
        /// <param name="userMessage">User message or data to analyze</param>
        /// <param name="temperature">Sampling temperature (0.0 = deterministic, 1.0 = creative). Default: 0.0</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Parsed JSON document</returns>
        /// <exception cref="HttpRequestException">If Gemini API returns an error</exception>
        /// <exception cref="InvalidOperationException">If response is not valid JSON</exception>
        Task<JsonDocument> GenerateJsonAsync(
            string systemPrompt,
            string userMessage,
            double temperature = 0.0,
            CancellationToken ct = default);
    }
}
