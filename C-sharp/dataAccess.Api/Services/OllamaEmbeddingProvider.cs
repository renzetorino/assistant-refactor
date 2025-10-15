using System.Net.Http.Json;
using System.Text.Json;
using dataAccess.Services;


namespace dataAccess.Api.Services
{
    public sealed class OllamaEmbeddingProvider : IEmbeddingProvider
    {
        private readonly HttpClient _http;
        private readonly string _model;

        public OllamaEmbeddingProvider(HttpClient http, IConfiguration cfg)
        {
            _http = http;
            _model = cfg["APP__EMBED__MODEL"] ?? "nomic-embed-text";

            if (_http.BaseAddress is null)
            {
                var baseUrl = cfg["APP__EMBED__BASEADDRESS"]
                              ?? Environment.GetEnvironmentVariable("APP__EMBED__BASEADDRESS")
                              ?? "http://localhost:11434/";
                _http.BaseAddress = new Uri(baseUrl);
            }
        }

        public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        {
            var payload = new { model = _model, prompt = text };
            using var resp = await _http.PostAsJsonAsync("api/embeddings", payload, ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);
            resp.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            // { "embedding": [...] }
            if (root.TryGetProperty("embedding", out var emb1) && emb1.ValueKind == JsonValueKind.Array)
                return emb1.EnumerateArray().Select(x => x.GetSingle()).ToArray();

            // { "data": [ { "embedding": [...] } ] }
            if (root.TryGetProperty("data", out var data) &&
                data.ValueKind == JsonValueKind.Array &&
                data.GetArrayLength() > 0 &&
                data[0].TryGetProperty("embedding", out var emb2) &&
                emb2.ValueKind == JsonValueKind.Array)
                return emb2.EnumerateArray().Select(x => x.GetSingle()).ToArray();

            throw new Exception("Unexpected Ollama embeddings response: " + raw);
        }
    }
}
