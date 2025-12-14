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

        // Safety thresholds (loaded from config, with fallback defaults)
        private readonly double _minConfidenceThreshold;
        private readonly double _chitchatConfidenceThreshold;

        // Allowed intents (security allowlist, loaded from config)
        private readonly HashSet<string> _allowedIntents;

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
            ILogger<YamlIntentRunner> logger,
            dataAccess.Planning.IntentClassificationConfig? intentConfig = null)
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

            // Load configuration with safe defaults
            _minConfidenceThreshold = intentConfig?.MinConfidence ?? 0.60;
            _chitchatConfidenceThreshold = intentConfig?.ChitchatConfidence ?? 0.55;

            // Load allowed intents from config or use defaults
            _allowedIntents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (intentConfig?.AllowedIntents?.Any() == true)
            {
                foreach (var intent in intentConfig.AllowedIntents)
                {
                    _allowedIntents.Add(intent);
                }
                _logger.LogInformation(
                    "[YamlIntentRunner] Loaded {Count} allowed intents from configuration",
                    _allowedIntents.Count
                );
            }
            else
            {
                // Fallback to hardcoded defaults
                var defaults = new[]
                {
                    "chitchat", "faq", "nlq.query",
                    "reports.inventory", "reports.expense", "reports.sales",
                    "forecast.sales", "forecast.inventory", "forecast.expense",
                    "out_of_scope"
                };
                foreach (var intent in defaults)
                {
                    _allowedIntents.Add(intent);
                }
                _logger.LogWarning(
                    "[YamlIntentRunner] Using default allowed intents (config not found)"
                );
            }

            _logger.LogInformation(
                "[YamlIntentRunner] Initialized with thresholds: min={MinConfidence}, chitchat={ChitchatConfidence}",
                _minConfidenceThreshold,
                _chitchatConfidenceThreshold
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

                var rawResponse = JsonDocument.Parse(doc.RootElement.GetRawText());

                // 6. Validate and sanitize response
                var validatedResponse = ValidateIntentResponse(rawResponse);

                return validatedResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[YamlIntentRunner] Intent classification failed");
                throw;
            }
        }

        /// <summary>
        /// Validates intent classification response for security and correctness.
        /// Applies confidence thresholds and intent allowlists.
        /// </summary>
        private JsonDocument ValidateIntentResponse(JsonDocument response)
        {
            try
            {
                var root = response.RootElement;
                
                // Extract intent and confidence
                string intent = root.GetProperty("intent").GetString() ?? "out_of_scope";
                double confidence = root.TryGetProperty("confidence", out var confProp)
                    ? confProp.GetDouble()
                    : 0.0;

                string? domain = root.TryGetProperty("domain", out var domProp) && domProp.ValueKind != JsonValueKind.Null
                    ? domProp.GetString()
                    : null;

                // Validate confidence threshold
                var validatedIntent = ValidateConfidence(intent, confidence);

                // Validate intent against allowlist
                validatedIntent = ValidateIntent(validatedIntent);

                // If intent was modified, log the change
                if (validatedIntent != intent)
                {
                    _logger.LogWarning(
                        "[YamlIntentRunner] Intent validation changed result: {Original} -> {Validated} (confidence: {Confidence})",
                        intent,
                        validatedIntent,
                        confidence
                    );
                }

                // Build validated response
                var validatedJson = JsonSerializer.Serialize(new
                {
                    intent = validatedIntent,
                    domain,
                    confidence,
                    original_intent = validatedIntent != intent ? intent : null,
                    validation_applied = validatedIntent != intent
                });

                return JsonDocument.Parse(validatedJson);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[YamlIntentRunner] Response validation failed, defaulting to chitchat");
                
                // Safest fallback
                var fallbackJson = JsonSerializer.Serialize(new
                {
                    intent = "chitchat",
                    domain = (string?)null,
                    confidence = 0.5,
                    validation_applied = true,
                    error = "validation_failed"
                });

                return JsonDocument.Parse(fallbackJson);
            }
        }

        /// <summary>
        /// Validates confidence score and applies fallback logic.
        /// Low confidence results default to chitchat for safety.
        /// </summary>
        private string ValidateConfidence(string intent, double confidence)
        {
            // If confidence is below minimum threshold, default to chitchat
            if (confidence < _chitchatConfidenceThreshold)
            {
                _logger.LogWarning(
                    "[YamlIntentRunner] Confidence {Confidence} below chitchat threshold {Threshold}, defaulting to chitchat",
                    confidence,
                    _chitchatConfidenceThreshold
                );
                return "chitchat";
            }

            // If confidence is between chitchat and minimum threshold, and intent is not chitchat, default to chitchat
            if (confidence < _minConfidenceThreshold && intent != "chitchat")
            {
                _logger.LogWarning(
                    "[YamlIntentRunner] Confidence {Confidence} below minimum threshold {Threshold}, defaulting to chitchat",
                    confidence,
                    _minConfidenceThreshold
                );
                return "chitchat";
            }

            return intent;
        }

        /// <summary>
        /// Validates intent against security allowlist.
        /// Unknown intents default to out_of_scope.
        /// </summary>
        private string ValidateIntent(string intent)
        {
            if (!_allowedIntents.Contains(intent))
            {
                _logger.LogWarning(
                    "[YamlIntentRunner] Intent '{Intent}' not in allowlist, defaulting to out_of_scope",
                    intent
                );
                return "out_of_scope";
            }

            return intent;
        }
    }
}
