using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace dataAccess.Services
{
    /// <summary>
    /// In-memory JSON-based FAQ service using local embeddings for semantic search.
    /// Replaces cloud-based Vertex AI RAG with a lightweight local solution.
    /// MUST be registered as Singleton to prevent re-embedding on every request.
    /// </summary>
    public class JsonFaqService
    {
        private readonly IEmbeddingService _embeddingService;
        private readonly ILogger<JsonFaqService> _logger;
        private readonly List<FaqEntry> _faqEntries = new();
        private bool _isInitialized = false;
        private readonly object _initLock = new();

        public JsonFaqService(IEmbeddingService embeddingService, ILogger<JsonFaqService> logger)
        {
            _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
        /// </summary>
        /// <param name="query">User's natural language question</param>
        /// <param name="threshold">Minimum similarity score (0-1). Default: 0.7</param>
        /// <returns>Best matching FAQ answer, or null if no good match</returns>
        public async Task<string?> SearchAsync(string query, double threshold = 0.7)
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
                    return null;
                }

                _logger.LogInformation($"Found FAQ match with similarity {bestMatch.Similarity:F3} for query: '{query}'");
                return bestMatch.Entry.Answer;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error searching FAQ for query: '{query}'");
                return null;
            }
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
}
