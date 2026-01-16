using Microsoft.Extensions.Configuration;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace dataAccess.LLM
{
    /// <summary>
    /// Google Gemini API client for generating structured JSON responses.
    /// Optimized for Business Mentor AI insights with retry logic and error handling.
    /// </summary>
    public sealed class GeminiJsonClient : IGeminiJsonClient
    {
        private readonly HttpClient _http;
        private readonly string _apiKey;
        private readonly string _model;

        private const int MAX_RETRIES = 3;
        private const int BASE_DELAY_MS = 1000;

        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        public GeminiJsonClient(HttpClient http, IConfiguration cfg)
        {
            _http = http;

            // Accept common env/config keys for Gemini API
            _apiKey =
                cfg["APP:GEMINI:API_KEY"]
                ?? cfg["APP__GEMINI__API_KEY"]
                ?? cfg["GEMINI:API_KEY"]
                ?? cfg["GEMINI_API_KEY"]
                ?? Environment.GetEnvironmentVariable("APP__GEMINI__API_KEY")
                ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY")
                ?? throw new InvalidOperationException("APP__GEMINI__API_KEY (or GEMINI_API_KEY) is not set.");

            // Allow model override via config; default to Gemini 3 Flash (fast, cost-effective)
            // Sprint 3: Updated from deprecated gemini-1.5-flash-latest to gemini-3-flash-preview
            _model = cfg["APP__GEMINI__MODEL"]
                ?? Environment.GetEnvironmentVariable("APP__GEMINI__MODEL")
                ?? "gemini-3-flash-preview";

            // Set base URL and headers
            _http.BaseAddress = new Uri("https://generativelanguage.googleapis.com/v1beta/");
            _http.Timeout = TimeSpan.FromSeconds(60);
            _http.DefaultRequestHeaders.Accept.Clear();
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        /// <summary>
        /// Generates a JSON response from Gemini with exponential backoff retry logic.
        /// </summary>
        public async Task<JsonDocument> GenerateJsonAsync(
            string systemPrompt,
            string userMessage,
            double temperature = 0.0,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(systemPrompt))
                throw new ArgumentException("System prompt cannot be null or empty", nameof(systemPrompt));

            if (string.IsNullOrWhiteSpace(userMessage))
                throw new ArgumentException("User message cannot be null or empty", nameof(userMessage));

            Exception? lastException = null;

            for (int attempt = 0; attempt < MAX_RETRIES; attempt++)
            {
                try
                {
                    return await GenerateJsonInternalAsync(systemPrompt, userMessage, temperature, ct);
                }
                catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    // Sprint 3: 429 = Quota exhausted, retrying won't help - fail immediately
                    throw new InvalidOperationException(
                        "Gemini API rate limit reached (429). Please wait before making more requests. " +
                        "Consider implementing request throttling or upgrading API quota.",
                        ex);
                }
                catch (HttpRequestException ex) when (
                    ex.StatusCode == HttpStatusCode.ServiceUnavailable ||
                    ex.StatusCode == HttpStatusCode.GatewayTimeout)
                {
                    // Sprint 3: These are transient server errors - retry makes sense
                    lastException = ex;

                    if (attempt < MAX_RETRIES - 1)
                    {
                        var delay = BASE_DELAY_MS * (int)Math.Pow(2, attempt);
                        await Task.Delay(delay, ct);
                        continue;
                    }

                    throw;
                }
                catch (Exception ex)
                {
                    // Non-retryable errors (auth, validation, etc.)
                    throw new InvalidOperationException($"Gemini API request failed: {ex.Message}", ex);
                }
            }

            throw new InvalidOperationException(
                $"Gemini API failed after {MAX_RETRIES} retries",
                lastException);
        }

        /// <summary>
        /// Internal method to call Gemini API and parse JSON response.
        /// </summary>
        private async Task<JsonDocument> GenerateJsonInternalAsync(
            string systemPrompt,
            string userMessage,
            double temperature,
            CancellationToken ct)
        {
            // Combine system prompt and user message (Gemini uses a single "parts" array)
            var combinedPrompt = $"{systemPrompt}\n\n{userMessage}";

            // Construct Gemini API request payload
            var requestPayload = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = combinedPrompt }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = temperature,
                    candidateCount = 1,
                    maxOutputTokens = 2048,
                    // Request JSON output format
                    responseMimeType = "application/json"
                }
            };

            var jsonPayload = JsonSerializer.Serialize(requestPayload, _jsonOpts);
            using var httpContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            // Construct URL with API key
            var url = $"models/{_model}:generateContent?key={_apiKey}";

            var response = await _http.PostAsync(url, httpContent, ct);
            var responseText = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Gemini API HTTP {(int)response.StatusCode} {response.StatusCode}: {responseText}",
                    null,
                    response.StatusCode);
            }

            // Parse Gemini response structure
            using var responseDoc = JsonDocument.Parse(responseText);
            
            // Extract text from: candidates[0].content.parts[0].text
            if (!responseDoc.RootElement.TryGetProperty("candidates", out var candidates) ||
                candidates.GetArrayLength() == 0)
            {
                throw new InvalidOperationException("Gemini response missing 'candidates' array");
            }

            var firstCandidate = candidates[0];
            if (!firstCandidate.TryGetProperty("content", out var content) ||
                !content.TryGetProperty("parts", out var parts) ||
                parts.GetArrayLength() == 0)
            {
                throw new InvalidOperationException("Gemini response missing 'content.parts'");
            }

            var textPart = parts[0];
            if (!textPart.TryGetProperty("text", out var textElement))
            {
                throw new InvalidOperationException("Gemini response missing 'text' in parts[0]");
            }

            var generatedText = textElement.GetString();
            if (string.IsNullOrWhiteSpace(generatedText))
            {
                throw new InvalidOperationException("Gemini returned empty text content");
            }

            // Parse the generated text as JSON
            return ParseAndValidateJson(generatedText);
        }

        /// <summary>
        /// Parses and validates that the response is valid JSON.
        /// </summary>
        private static JsonDocument ParseAndValidateJson(string text)
        {
            try
            {
                // Try parsing as-is (Gemini should return clean JSON due to responseMimeType)
                return JsonDocument.Parse(text);
            }
            catch (JsonException)
            {
                // Fallback: strip markdown code fences if present
                var cleaned = text.Trim();
                if (cleaned.StartsWith("```json"))
                    cleaned = cleaned.Substring(7);
                else if (cleaned.StartsWith("```"))
                    cleaned = cleaned.Substring(3);

                if (cleaned.EndsWith("```"))
                    cleaned = cleaned.Substring(0, cleaned.Length - 3);

                cleaned = cleaned.Trim();

                try
                {
                    return JsonDocument.Parse(cleaned);
                }
                catch (JsonException ex)
                {
                    throw new InvalidOperationException(
                        $"Gemini returned invalid JSON. Raw text: {text}",
                        ex);
                }
            }
        }
    }
}
