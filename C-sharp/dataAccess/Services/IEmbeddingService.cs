using System.Threading.Tasks;

namespace dataAccess.Services
{
    /// <summary>
    /// Service for generating text embeddings (vector representations).
    /// Used for semantic similarity search in RAG system.
    /// </summary>
    public interface IEmbeddingService
    {
        /// <summary>
        /// Generate 384-dimensional embedding vector for input text.
        /// </summary>
        /// <param name="text">Input text to embed</param>
        /// <returns>384-dimensional float array</returns>
        Task<float[]> GetEmbeddingAsync(string text);
    }
}
