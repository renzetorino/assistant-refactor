using dataAccess.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dataAccess.Services
{
    /// <summary>
    /// Local decoder service for generating natural language responses using Phi-3-mini-4k-instruct ONNX model.
    /// Replaces cloud-based LLM calls for chitchat and FAQ intents with local inference.
    /// NO API CALLS - runs entirely on local CPU/GPU.
    /// </summary>
    public interface ILocalDecoderService
    {
        /// <summary>
        /// Generate a natural language response using local Phi-3 model.
        /// </summary>
        /// <param name="userQuery">The user's current question or message</param>
        /// <param name="history">Chat history for conversational context (optional)</param>
        /// <param name="intent">The detected intent (e.g., "chitchat", "faq") to select appropriate prompt template</param>
        /// <returns>Generated response text from the local model</returns>
        Task<string> GetResponseAsync(string userQuery, List<ChatMessage> history, string intent);
    }
}
