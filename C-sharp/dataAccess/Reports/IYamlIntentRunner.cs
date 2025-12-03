using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using dataAccess.Entities;

namespace dataAccess.Reports;

/// <summary>
/// Interface for intent classification using YAML-driven prompts.
/// Enables mocking in tests while maintaining loose coupling.
/// </summary>
public interface IYamlIntentRunner
{
    /// <summary>
    /// Classifies user intent using the router.intent.yaml configuration.
    /// Supports conversational memory by accepting chat history for context.
    /// </summary>
    /// <param name="userText">The user's natural language query</param>
    /// <param name="history">Optional chat history for context (recent messages)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>JsonDocument containing intent, domain, sub_intent, and confidence</returns>
    Task<JsonDocument> RunIntentAsync(string userText, List<ChatMessage>? history, CancellationToken ct);
}
