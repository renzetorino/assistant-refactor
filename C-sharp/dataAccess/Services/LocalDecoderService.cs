using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using dataAccess.Entities;
using dataAccess.LLM;

namespace dataAccess.Services
{
    /// <summary>
    /// Cloud-native decoder service using Groq API (llama-3.1-8b-instant) for natural language response generation.
    /// Supports chitchat and FAQ intents with conversational context awareness.
    /// REFACTORED FOR LOW-RAM DEPLOYMENT - Uses Groq API instead of local ONNX inference.
    /// </summary>
    public class LocalDecoderService : ILocalDecoderService
    {
        private readonly GroqJsonClient _groq;
        private readonly ILogger<LocalDecoderService> _logger;

        public LocalDecoderService(GroqJsonClient groq, ILogger<LocalDecoderService> logger)
        {
            _groq = groq ?? throw new ArgumentNullException(nameof(groq));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _logger.LogInformation(
                "[LocalDecoderService] Initialized with Groq API (llama-3.1-8b-instant) for cloud-native inference."
            );
        }

        public async Task<string> GetResponseAsync(string userQuery, List<ChatMessage> history, string intent)
        {
            try
            {
                // 1. Load appropriate prompt template based on intent
                var systemPrompt = LoadPromptTemplate(intent);

                // 2. Format the prompt with chat history and user query for Groq API
                var formattedUserMessage = FormatUserMessage(userQuery, history);

                _logger.LogDebug(
                    "[LocalDecoderService] Calling Groq API for intent '{Intent}' with system prompt from {PromptFile}",
                    intent,
                    intent == "chitchat" ? "responder.chitchat.yaml" : "responder.faq.yaml"
                );

                // 3. Call Groq API with llama-3.1-8b-instant model
                // Use CompleteJsonAsyncChat to maintain consistency with other chat-based services
                using var responseDoc = await _groq.CompleteJsonAsyncChat(
                    system: systemPrompt,
                    user: formattedUserMessage,
                    history: history, // Pass full history for conversational context
                    temperature: 0.7, // Slightly creative for natural responses
                    ct: default
                );

                // 4. Extract response text from JSON
                var responseText = ExtractResponseText(responseDoc);

                _logger.LogInformation(
                    "[LocalDecoderService] Generated response for intent '{Intent}' (length: {Length})",
                    intent,
                    responseText.Length
                );

                return responseText;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[LocalDecoderService] Error generating response for intent '{Intent}'", intent);
                
                // Fallback response
                return intent switch
                {
                    "chitchat" => "Hello! How can I help you with your business today?",
                    "faq" => "I can help you with sales reports, expense tracking, inventory management, and forecasting. What would you like to know?",
                    _ => "I'm here to help with your business needs. What can I assist you with?"
                };
            }
        }

        /// <summary>
        /// Load prompt template from YAML file based on intent
        /// </summary>
        private string LoadPromptTemplate(string intent)
        {
            var promptFileName = intent.ToLowerInvariant() switch
            {
                "chitchat" => "responder.chitchat.yaml",
                "faq" => "responder.faq.yaml",
                _ => "responder.chitchat.yaml" // Default to chitchat
            };

            var promptPath = Path.Combine(
                AppContext.BaseDirectory,
                "Planning",
                "Prompts",
                promptFileName
            );

            if (!File.Exists(promptPath))
            {
                _logger.LogWarning(
                    "[LocalDecoderService] Prompt template not found: {Path}, using default",
                    promptPath
                );
                return "You are BuiswAIz, a friendly and helpful business assistant. Answer the user's question clearly and concisely.";
            }

            try
            {
                var yamlContent = File.ReadAllText(promptPath);
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(UnderscoredNamingConvention.Instance)
                    .Build();
                
                var promptConfig = deserializer.Deserialize<Dictionary<string, object>>(yamlContent);
                
                // Extract the system prompt from the YAML
                if (promptConfig.TryGetValue("system", out var systemPrompt))
                {
                    return systemPrompt.ToString()?.Trim() ?? string.Empty;
                }
                
                _logger.LogWarning("[LocalDecoderService] No 'system' field in prompt template, using default");
                return "You are BuiswAIz, a friendly and helpful business assistant.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[LocalDecoderService] Error loading prompt template: {Path}", promptPath);
                return "You are BuiswAIz, a friendly and helpful business assistant.";
            }
        }

        /// <summary>
        /// Format user message with optional chat history context (simple concatenation)
        /// The Groq API client will handle proper message formatting internally.
        /// </summary>
        private string FormatUserMessage(string userQuery, List<ChatMessage>? history)
        {
            // For Groq API, we simply pass the current query
            // The history is passed separately via the API client's history parameter
            return userQuery;
        }

        /// <summary>
        /// Extract response text from Groq JSON response.
        /// Expects format: { "response": "text" } or { "answer": "text" } or { "message": "text" }
        /// </summary>
        private string ExtractResponseText(System.Text.Json.JsonDocument responseDoc)
        {
            try
            {
                var root = responseDoc.RootElement;

                // Try common response keys
                if (root.TryGetProperty("response", out var respEl) && respEl.ValueKind == System.Text.Json.JsonValueKind.String)
                    return respEl.GetString() ?? string.Empty;

                if (root.TryGetProperty("answer", out var ansEl) && ansEl.ValueKind == System.Text.Json.JsonValueKind.String)
                    return ansEl.GetString() ?? string.Empty;

                if (root.TryGetProperty("message", out var msgEl) && msgEl.ValueKind == System.Text.Json.JsonValueKind.String)
                    return msgEl.GetString() ?? string.Empty;

                if (root.TryGetProperty("text", out var txtEl) && txtEl.ValueKind == System.Text.Json.JsonValueKind.String)
                    return txtEl.GetString() ?? string.Empty;

                // Fallback: serialize entire JSON as string (for debugging)
                _logger.LogWarning(
                    "[LocalDecoderService] Unexpected Groq response format. Full JSON: {Json}",
                    responseDoc.RootElement.GetRawText()
                );

                return responseDoc.RootElement.GetRawText();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[LocalDecoderService] Failed to extract response text from Groq JSON");
                return "I apologize, but I encountered an issue processing the response.";
            }
        }
    }
}
