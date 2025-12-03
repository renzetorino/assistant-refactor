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
    /// Retrieves relevant intent classification examples using semantic similarity search.
    /// Implements RAG (Retrieval-Augmented Generation) for intent classification.
    /// </summary>
    public class IntentExampleRetriever
    {
        private readonly IEmbeddingService _embeddingService;
        private readonly ILogger<IntentExampleRetriever> _logger;
        private readonly string _examplesPath;
        private List<IntentExample>? _cachedExamples;
        private readonly object _cacheLock = new object();

        public IntentExampleRetriever(
            IEmbeddingService embeddingService,
            ILogger<IntentExampleRetriever> logger)
        {
            _embeddingService = embeddingService;
            _logger = logger;
            _examplesPath = Path.Combine(
                AppContext.BaseDirectory,
                "Planning",
                "Prompts",
                "intent.examples.json"
            );
        }

        /// <summary>
        /// Get the top K most relevant examples for a given user query.
        /// Uses cosine similarity between query embedding and example embeddings.
        /// </summary>
        /// <param name="userQuery">User's input text</param>
        /// <param name="topK">Number of examples to return (default: 5)</param>
        /// <param name="minSimilarity">Minimum similarity threshold (default: 0.5)</param>
        /// <returns>List of most relevant examples, sorted by similarity</returns>
        public async Task<List<IntentExample>> GetRelevantExamplesAsync(
            string userQuery,
            int topK = 5,
            float minSimilarity = 0.5f)
        {
            try
            {
                // 1. Load examples (cached in memory after first call)
                var examples = await LoadExamplesAsync();

                if (examples.Count == 0)
                {
                    _logger.LogWarning("[RAG] No examples found in intent.examples.json");
                    return new List<IntentExample>();
                }

                // 2. Generate query embedding
                var queryEmbedding = await _embeddingService.GetEmbeddingAsync(userQuery);

                // 3. Compute cosine similarity with all examples
                var similarities = examples
                    .Select(ex => new
                    {
                        Example = ex,
                        Similarity = CosineSimilarity(queryEmbedding, ex.Embedding)
                    })
                    .Where(x => x.Similarity >= minSimilarity)
                    .OrderByDescending(x => x.Similarity)
                    .Take(topK)
                    .ToList();

                _logger.LogInformation(
                    "[RAG] Retrieved {Count} relevant examples for query: \"{Query}\" " +
                    "(top similarity: {TopScore:F3})",
                    similarities.Count,
                    userQuery,
                    similarities.FirstOrDefault()?.Similarity ?? 0f
                );

                return similarities.Select(s => s.Example).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RAG] Failed to retrieve relevant examples");
                return new List<IntentExample>();
            }
        }

        /// <summary>
        /// Load examples from JSON file (cached in memory).
        /// </summary>
        private async Task<List<IntentExample>> LoadExamplesAsync()
        {
            if (_cachedExamples != null)
            {
                return _cachedExamples;
            }

            lock (_cacheLock)
            {
                // Double-check after acquiring lock
                if (_cachedExamples != null)
                {
                    return _cachedExamples;
                }

                if (!File.Exists(_examplesPath))
                {
                    _logger.LogError(
                        "[RAG] intent.examples.json not found at: {Path}",
                        _examplesPath
                    );
                    _cachedExamples = new List<IntentExample>();
                    return _cachedExamples;
                }

                var json = File.ReadAllText(_examplesPath);
                var data = JsonSerializer.Deserialize<IntentExamplesData>(
                    json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );

                _cachedExamples = data?.Examples ?? new List<IntentExample>();

                _logger.LogInformation(
                    "[RAG] Loaded {Count} examples from intent.examples.json " +
                    "(embedding model: {Model}, dimensions: {Dim})",
                    _cachedExamples.Count,
                    data?.Metadata?.EmbeddingModel ?? "unknown",
                    data?.Metadata?.EmbeddingDimensions ?? 0
                );

                return _cachedExamples;
            }
        }

        /// <summary>
        /// Compute cosine similarity between two vectors.
        /// Returns value between -1 (opposite) and 1 (identical).
        /// </summary>
        private float CosineSimilarity(float[] a, float[] b)
        {
            if (a.Length != b.Length)
            {
                throw new ArgumentException(
                    $"Vector dimension mismatch: {a.Length} vs {b.Length}"
                );
            }

            float dotProduct = 0f;
            float magnitudeA = 0f;
            float magnitudeB = 0f;

            for (int i = 0; i < a.Length; i++)
            {
                dotProduct += a[i] * b[i];
                magnitudeA += a[i] * a[i];
                magnitudeB += b[i] * b[i];
            }

            float magnitude = MathF.Sqrt(magnitudeA) * MathF.Sqrt(magnitudeB);

            return magnitude > 0 ? dotProduct / magnitude : 0f;
        }

        /// <summary>
        /// Clear cached examples (useful for hot-reload during development).
        /// </summary>
        public void ClearCache()
        {
            lock (_cacheLock)
            {
                _cachedExamples = null;
                _logger.LogInformation("[RAG] Example cache cleared");
            }
        }

        /// <summary>
        /// Get statistics about the loaded examples.
        /// </summary>
        public async Task<ExampleStats> GetStatsAsync()
        {
            var examples = await LoadExamplesAsync();

            var intentGroups = examples.GroupBy(e => e.Intent)
                .Select(g => new IntentCount
                {
                    Intent = g.Key,
                    Count = g.Count()
                })
                .OrderByDescending(ic => ic.Count)
                .ToList();

            return new ExampleStats
            {
                TotalExamples = examples.Count,
                IntentDistribution = intentGroups,
                HasEmbeddings = examples.All(e => e.Embedding?.Length == 384)
            };
        }
    }

    // Model classes
    public class IntentExamplesData
    {
        public IntentMetadata? Metadata { get; set; }
        public List<IntentExample> Examples { get; set; } = new();
    }

    public class IntentMetadata
    {
        public string Version { get; set; } = "";
        public string EmbeddingModel { get; set; } = "";
        public int EmbeddingDimensions { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    public class IntentExample
    {
        public int Id { get; set; }
        public string Input { get; set; } = "";
        public string Intent { get; set; } = "";
        public string? Domain { get; set; }
        public float Confidence { get; set; }
        public List<string> Tags { get; set; } = new();
        public string? Context { get; set; }
        public float[] Embedding { get; set; } = Array.Empty<float>();
    }

    public class ExampleStats
    {
        public int TotalExamples { get; set; }
        public List<IntentCount> IntentDistribution { get; set; } = new();
        public bool HasEmbeddings { get; set; }
    }

    public class IntentCount
    {
        public string Intent { get; set; } = "";
        public int Count { get; set; }
    }
}
