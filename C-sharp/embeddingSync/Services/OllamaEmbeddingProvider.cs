using System.Net.Http.Json;
using System.Text.Json;

namespace EmbeddingSync.Services
{
    public interface IEmbeddingProvider
    {
        Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
    }

    public sealed class OllamaEmbeddingProvider : IEmbeddingProvider
    {
        private readonly HttpClient _http;
        private readonly string _model;

        public OllamaEmbeddingProvider(HttpClient http, IConfiguration cfg)
        {
            _http = http;
            _model = cfg["APP__EMBED__MODEL"] ?? "nomic-embed-text";
            if (_http.BaseAddress == null)
            {
                var baseUrl = cfg["APP__EMBED__BASEADDRESS"] ?? cfg["APP__EMBED__ENDPOINT"] ?? "http://localhost:11434/";
                _http.BaseAddress = new Uri(baseUrl);
            }
        }

        public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("input text is empty", nameof(text));

            // sanity log
            Console.WriteLine("[Ollama] BaseAddress=" + _http.BaseAddress);

            var payload = new { model = _model, prompt = text };

            using var resp = await _http.PostAsJsonAsync("api/embeddings", payload, ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"❌ Ollama error {resp.StatusCode}: {raw}");

            try
            {
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;

                // shape 1: { "embedding": [...] }
                if (root.TryGetProperty("embedding", out var emb1) && emb1.ValueKind == JsonValueKind.Array)
                {
                    var vec1 = ToFloatArray(emb1);
                    if (vec1.Length == 0) throw new Exception("❌ Ollama returned empty embedding vector.");
                    return vec1;
                }

                // shape 2: { "data": [ { "embedding": [...] } ] }
                if (root.TryGetProperty("data", out var data) &&
                    data.ValueKind == JsonValueKind.Array &&
                    data.GetArrayLength() > 0)
                {
                    var first = data[0];
                    if (first.TryGetProperty("embedding", out var emb2) &&
                        emb2.ValueKind == JsonValueKind.Array)
                    {
                        var vec2 = ToFloatArray(emb2);
                        if (vec2.Length == 0) throw new Exception("❌ Ollama returned empty embedding vector.");
                        return vec2;
                    }
                }

                Console.WriteLine("[Ollama RAW] " + raw);
                throw new Exception("❌ Unexpected Ollama response shape.");
            }
            catch (JsonException)
            {
                Console.WriteLine("[Ollama RAW] " + raw);
                throw;
            }
        }

        private static float[] ToFloatArray(JsonElement arr)
        {
            var list = new List<float>(768);
            foreach (var el in arr.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.Number)
                {
                    if (el.TryGetSingle(out float f)) list.Add(f);
                    else list.Add((float)el.GetDouble());
                }
            }
            return list.ToArray();
        }
    }
}
