using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using dataAccess.Entities;
using dataAccess.LLM;
using dataAccess.Services;
using System.Text.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using Microsoft.Extensions.Logging;

namespace dataAccess.Reports
{
    /// <summary>
    /// Intent classification runner with RAG optimization.
    /// Retrieves only relevant examples dynamically instead of sending all 50+ examples.
    /// Supports conversational memory for context-aware intent classification.
    /// </summary>
    public class YamlIntentRunner : IYamlIntentRunner
    {
        private readonly GroqJsonClient _groq;
        private readonly IntentExampleRetriever _exampleRetriever;
        private readonly ILogger<YamlIntentRunner> _logger;
        private readonly string _intentYamlPath;

        // Fallback examples (used if RAG fails)
        private static readonly List<string> _fallbackExamples = new()
        {
            "- input: \"yo\"\n  output: { \"intent\": \"chitchat\", \"confidence\": 0.95 }",
            "- input: \"show sales report\"\n  output: { \"intent\": \"reports.sales\", \"confidence\": 0.95 }",
            "- input: \"forecast next week\"\n  output: { \"intent\": \"forecast.sales\", \"confidence\": 0.9 }",
            "- input: \"How do I create a report?\"\n  output: { \"intent\": \"faq\", \"confidence\": 0.95 }",
            "- input: \"How to cook eggs?\"\n  output: { \"intent\": \"out_of_scope\", \"confidence\": 0.98 }"
        };

        public YamlIntentRunner(
            GroqJsonClient groq,
            IntentExampleRetriever exampleRetriever,
            ILogger<YamlIntentRunner> logger)
        {
            _groq = groq;
            _exampleRetriever = exampleRetriever;
            _logger = logger;
            _intentYamlPath = Path.Combine(
                AppContext.BaseDirectory,
                "Planning",
                "Prompts",
                "router.intent.yaml"
            );
        }

        public async Task<JsonDocument> RunIntentAsync(
            string userText,
            List<ChatMessage>? history,
            CancellationToken ct)
        {
            try
            {
                // 1. Load optimized YAML (without hardcoded examples)
                var yaml = await File.ReadAllTextAsync(_intentYamlPath, ct);
                var des = new DeserializerBuilder().Build();
                var yamlObj = des.Deserialize<dynamic>(yaml);
                
                string systemPrompt = yamlObj["system"] ?? "You are a strict intent classifier.";
                double temperature = 0.0;
                
                try
                {
                    var tempObj = yamlObj["defaults"]?["model"]?["temperature"];
                    if (tempObj != null)
                        temperature = Convert.ToDouble(tempObj);
                }
                catch { }

                // 2. RAG: Retrieve relevant examples dynamically
                List<IntentExample> relevantExamples;
                try
                {
                    relevantExamples = await _exampleRetriever.GetRelevantExamplesAsync(
                        userText,
                        topK: 5, // Retrieve top 5 most similar examples
                        minSimilarity: 0.6f
                    );

                    _logger.LogInformation(
                        "[YamlIntentRunner] RAG retrieved {Count} relevant examples for: \"{Query}\"",
                        relevantExamples.Count,
                        userText
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "[YamlIntentRunner] RAG failed, using fallback examples"
                    );
                    relevantExamples = new List<IntentExample>();
                }

                // 3. Build dynamic examples section
                string examplesSection;
                if (relevantExamples.Any())
                {
                    examplesSection = "\n\nRelevant Examples:\n" + string.Join("\n",
                        relevantExamples.Select(ex =>
                            $"- input: \"{ex.Input}\"\n" +
                            $"  output: {{ \"intent\": \"{ex.Intent}\", " +
                            $"\"domain\": {(ex.Domain != null ? $"\"{ex.Domain}\"" : "null")}, " +
                            $"\"confidence\": {ex.Confidence} }}"
                        )
                    );
                }
                else
                {
                    // Fallback to 5 hardcoded examples if RAG fails
                    examplesSection = "\n\nFallback Examples:\n" + string.Join("\n", _fallbackExamples);
                    _logger.LogWarning("[YamlIntentRunner] Using fallback examples (RAG unavailable)");
                }

                // 4. Combine system prompt with dynamic examples
                string enhancedSystemPrompt = systemPrompt + examplesSection;

                _logger.LogDebug(
                    "[YamlIntentRunner] Enhanced prompt size: ~{Size} tokens",
                    (enhancedSystemPrompt.Length + userText.Length) / 4 // Rough estimate
                );

                // 5. Call Groq with optimized prompt
                using var doc = await _groq.CompleteJsonAsyncChat(
                    enhancedSystemPrompt,
                    userText,
                    history,
                    temperature,
                    ct
                );

                return JsonDocument.Parse(doc.RootElement.GetRawText());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[YamlIntentRunner] Intent classification failed");
                throw;
            }
        }
    }
}
