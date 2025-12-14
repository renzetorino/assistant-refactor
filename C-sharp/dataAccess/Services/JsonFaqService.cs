using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using dataAccess.Entities;

namespace dataAccess.Services
{
    /// <summary>
    /// In-memory JSON-based FAQ service using local embeddings for semantic search.
    /// Replaces cloud-based Vertex AI RAG with a lightweight local solution.
    /// MUST be registered as Singleton to prevent re-embedding on every request.
    /// </summary>
    public class JsonFaqService : IJsonFaqService
    {
        private readonly IEmbeddingService _embeddingService;
        private readonly ILogger<JsonFaqService> _logger;
        private readonly AiDbContext _aiDbContext;
        private readonly List<FaqEntry> _faqEntries = new();
        private bool _isInitialized = false;
        private readonly object _initLock = new();

        public JsonFaqService(
            IEmbeddingService embeddingService, 
            ILogger<JsonFaqService> logger,
            AiDbContext aiDbContext)
        {
            _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _aiDbContext = aiDbContext ?? throw new ArgumentNullException(nameof(aiDbContext));
        }

        /// <summary>
        /// Lazy initialization: Load and embed FAQ data on first query.
        /// Thread-safe singleton pattern ensures this runs only once.
        /// </summary>
        private async Task EnsureInitializedAsync()
        {
            if (_isInitialized) return;

            lock (_initLock)
            {
                if (_isInitialized) return;

                // Double-check pattern for async initialization
                _logger.LogInformation("Initializing JsonFaqService...");
            }

            // Load FAQ data
            var faqPath = Path.Combine(AppContext.BaseDirectory, "Data", "faq.json");
            
            if (!File.Exists(faqPath))
            {
                _logger.LogWarning($"FAQ file not found at {faqPath}. Service will return no results.");
                _isInitialized = true;
                return;
            }

            try
            {
                var jsonContent = await File.ReadAllTextAsync(faqPath);
                var faqData = JsonSerializer.Deserialize<FaqData>(jsonContent, new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true 
                });

                if (faqData?.Faqs == null || !faqData.Faqs.Any())
                {
                    _logger.LogWarning("FAQ file is empty or invalid.");
                    _isInitialized = true;
                    return;
                }

                // Generate embeddings for all questions
                _logger.LogInformation($"Generating embeddings for {faqData.Faqs.Count} FAQ entries...");
                
                foreach (var faq in faqData.Faqs)
                {
                    if (string.IsNullOrWhiteSpace(faq.Question))
                    {
                        _logger.LogWarning("Skipping FAQ entry with empty question.");
                        continue;
                    }

                    var embedding = await _embeddingService.GetEmbeddingAsync(faq.Question);
                    
                    _faqEntries.Add(new FaqEntry
                    {
                        Question = faq.Question,
                        Answer = faq.Answer ?? "No answer available.",
                        Embedding = embedding
                    });
                }

                _logger.LogInformation($"Successfully initialized {_faqEntries.Count} FAQ entries with embeddings.");
                _isInitialized = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize FAQ service.");
                _isInitialized = true; // Prevent infinite retry
            }
        }

        /// <summary>
        /// Search for the most relevant FAQ answer using cosine similarity.
        /// Returns null if no match is found or similarity is below threshold.
        /// This method is maintained for backward compatibility and calls SearchWithAnalyticsAsync internally.
        /// </summary>
        /// <param name="query">User's natural language question</param>
        /// <param name="threshold">Minimum similarity score (0-1). Default: 0.7</param>
        /// <returns>Best matching FAQ answer, or null if no good match</returns>
        public async Task<string?> SearchAsync(string query, double threshold = 0.7)
        {
            // Call enhanced method with default userId (Guid.Empty indicates no user tracking)
            var result = await SearchWithAnalyticsAsync(query, Guid.Empty, threshold);
            return result?.Answer;
        }

        /// <summary>
        /// Search for the most relevant FAQ answer with analytics tracking and confidence scoring.
        /// Logs search queries to FaqSearchLog table for analytics.
        /// </summary>
        /// <param name="query">User's natural language question</param>
        /// <param name="userId">User ID for analytics tracking</param>
        /// <param name="threshold">Minimum similarity score (0-1). Default: 0.7</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>FAQ result with answer, confidence score, and question, or null if no match</returns>
        public async Task<FaqSearchResult?> SearchWithAnalyticsAsync(
            string query, 
            Guid userId, 
            double threshold = 0.7,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                _logger.LogWarning("Empty query provided to FAQ search.");
                return null;
            }

            // Ensure FAQ data is loaded and embedded
            await EnsureInitializedAsync();

            if (!_faqEntries.Any())
            {
                _logger.LogWarning("No FAQ entries available for search.");
                return null;
            }

            try
            {
                // Generate embedding for the user query
                var queryEmbedding = await _embeddingService.GetEmbeddingAsync(query);

                // Find the most similar FAQ using cosine similarity
                var bestMatch = _faqEntries
                    .Select(entry => new
                    {
                        Entry = entry,
                        Similarity = CosineSimilarity(queryEmbedding, entry.Embedding)
                    })
                    .OrderByDescending(x => x.Similarity)
                    .FirstOrDefault();

                if (bestMatch == null || bestMatch.Similarity < threshold)
                {
                    _logger.LogInformation($"No FAQ match found for query: '{query}' (best similarity: {bestMatch?.Similarity:F3})");
                    
                    // Log failed search for analytics
                    await LogFaqSearchAsync(userId, query, null, bestMatch?.Similarity ?? 0, ct);
                    
                    return null;
                }

                _logger.LogInformation($"Found FAQ match with similarity {bestMatch.Similarity:F3} for query: '{query}'");
                
                // Log successful search for analytics
                await LogFaqSearchAsync(
                    userId, 
                    query, 
                    bestMatch.Entry.Answer, 
                    bestMatch.Similarity,
                    ct);

                return new FaqSearchResult
                {
                    Question = bestMatch.Entry.Question,
                    Answer = bestMatch.Entry.Answer,
                    Confidence = bestMatch.Similarity
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error searching FAQ for query: '{query}'");
                return null;
            }
        }

        /// <summary>
        /// Get similar questions when no strong match is found (fallback chain).
        /// Returns top N similar questions for "Did you mean?" suggestions.
        /// </summary>
        /// <param name="query">User's natural language question</param>
        /// <param name="topN">Number of suggestions to return. Default: 3</param>
        /// <param name="minThreshold">Minimum similarity for suggestions. Default: 0.4</param>
        /// <returns>List of similar questions with confidence scores</returns>
        public async Task<List<FaqSuggestion>> GetSimilarQuestionsAsync(
            string query, 
            int topN = 3, 
            double minThreshold = 0.4)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                _logger.LogWarning("Empty query provided to FAQ suggestions.");
                return new List<FaqSuggestion>();
            }

            // Ensure FAQ data is loaded and embedded
            await EnsureInitializedAsync();

            if (!_faqEntries.Any())
            {
                _logger.LogWarning("No FAQ entries available for suggestions.");
                return new List<FaqSuggestion>();
            }

            try
            {
                // Generate embedding for the user query
                var queryEmbedding = await _embeddingService.GetEmbeddingAsync(query);

                // Find top N similar FAQs
                var suggestions = _faqEntries
                    .Select(entry => new FaqSuggestion
                    {
                        Question = entry.Question,
                        Confidence = CosineSimilarity(queryEmbedding, entry.Embedding)
                    })
                    .Where(x => x.Confidence >= minThreshold)
                    .OrderByDescending(x => x.Confidence)
                    .Take(topN)
                    .ToList();

                _logger.LogInformation(
                    $"Found {suggestions.Count} similar questions for query: '{query}' (min threshold: {minThreshold})");

                return suggestions;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting similar questions for query: '{query}'");
                return new List<FaqSuggestion>();
            }
        }

        /// <summary>
        /// Log FAQ search to database for analytics tracking.
        /// </summary>
        private async Task LogFaqSearchAsync(
            Guid userId, 
            string query, 
            string? answer, 
            double confidence,
            CancellationToken ct)
        {
            try
            {
                // Skip logging if userId is empty (backward compatibility mode)
                if (userId == Guid.Empty)
                {
                    return;
                }

                var log = new FaqSearchLog
                {
                    UserId = userId,
                    Query = query,
                    Intent = "faq",
                    AnswerSnippet = answer != null ? TruncateAnswer(answer, 200) : null,
                    Confidence = (decimal)confidence,
                    CreatedAt = DateTime.UtcNow
                };

                _aiDbContext.FaqSearchLogs.Add(log);
                await _aiDbContext.SaveChangesAsync(ct);

                _logger.LogDebug($"Logged FAQ search for user {userId}: query='{query}', confidence={confidence:F3}");
            }
            catch (Exception ex)
            {
                // Don't fail the search if logging fails
                _logger.LogWarning(ex, "Failed to log FAQ search to database.");
            }
        }

        /// <summary>
        /// Truncate answer to specified length for database storage.
        /// </summary>
        private static string TruncateAnswer(string answer, int maxLength)
        {
            if (answer.Length <= maxLength)
            {
                return answer;
            }

            return answer.Substring(0, maxLength - 3) + "...";
        }

        /// <summary>
        /// Calculate cosine similarity between two vectors.
        /// Returns value between -1 (opposite) and 1 (identical).
        /// </summary>
        private static double CosineSimilarity(float[] vectorA, float[] vectorB)
        {
            if (vectorA.Length != vectorB.Length)
                throw new ArgumentException("Vectors must have the same dimension.");

            double dotProduct = 0;
            double magnitudeA = 0;
            double magnitudeB = 0;

            for (int i = 0; i < vectorA.Length; i++)
            {
                dotProduct += vectorA[i] * vectorB[i];
                magnitudeA += vectorA[i] * vectorA[i];
                magnitudeB += vectorB[i] * vectorB[i];
            }

            magnitudeA = Math.Sqrt(magnitudeA);
            magnitudeB = Math.Sqrt(magnitudeB);

            if (magnitudeA == 0 || magnitudeB == 0)
                return 0;

            return dotProduct / (magnitudeA * magnitudeB);
        }

        /// <summary>
        /// Internal representation of an FAQ entry with its embedding.
        /// </summary>
        private class FaqEntry
        {
            public string Question { get; set; } = string.Empty;
            public string Answer { get; set; } = string.Empty;
            public float[] Embedding { get; set; } = Array.Empty<float>();
        }

        /// <summary>
        /// JSON deserialization model for faq.json file.
        /// </summary>
        private class FaqData
        {
            public List<FaqItem> Faqs { get; set; } = new();
        }

        private class FaqItem
        {
            public string Question { get; set; } = string.Empty;
            public string Answer { get; set; } = string.Empty;
        }
    }

    /// <summary>
    /// Result from FAQ search with confidence scoring.
    /// </summary>
    public class FaqSearchResult
    {
        public string Question { get; set; } = string.Empty;
        public string Answer { get; set; } = string.Empty;
        public double Confidence { get; set; }
    }

    /// <summary>
    /// Suggested FAQ question for fallback chain.
    /// </summary>
    public class FaqSuggestion
    {
        public string Question { get; set; } = string.Empty;
        public double Confidence { get; set; }
    }
}
