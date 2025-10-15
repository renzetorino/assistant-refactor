using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Capcap.Services;

public sealed class GroqChatLlm : IChatLlm
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly bool _hasKey;

    public GroqChatLlm(HttpClient http, IConfiguration cfg)
    {
        _http = http;

        // Read base URL from config or env, then normalize it
        var baseUrlRaw =
            cfg["APP__GROQ__BASE_URL"] ??
            Environment.GetEnvironmentVariable("APP__GROQ__BASE_URL") ??
            "https://api.groq.com/openai/v1";

        var baseUrl = baseUrlRaw.Trim();
        if (!baseUrl.EndsWith("/")) baseUrl += "/";               // 👈 important
        _http.BaseAddress = new Uri(baseUrl, UriKind.Absolute);

        // Set a reasonable timeout (optional but recommended)
        _http.Timeout = TimeSpan.FromSeconds(12);

        var apiKey =
            cfg["APP__GROQ__API_KEY"] ??
            Environment.GetEnvironmentVariable("APP__GROQ__API_KEY");
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            _http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            _hasKey = true;
        }

        _model =
            cfg["APP__GROQ__MODEL"] ??
            Environment.GetEnvironmentVariable("APP__GROQ__MODEL") ??
            "gemma2-9b-it";
        Console.WriteLine($"[Groq] BaseAddress = '{_http.BaseAddress}'");
    }


    public async Task<string> ChatAsync(string system, string user, CancellationToken ct = default)
    {
        if (!_hasKey) return $"[groq-disabled] {user}"; // ← no 500s when key missing

        var payload = new
        {
            model = _model,
            messages = new[] {
                new { role = "system", content = system },
                new { role = "user",   content = user }
            },
            temperature = 0.2
        };

        using var resp = await _http.PostAsJsonAsync("chat/completions", payload, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) return $"[groq-error {resp.StatusCode}] {text}";

        using var doc = System.Text.Json.JsonDocument.Parse(text);
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
    }

    public async Task<string> ClassifyAsync(string system, string user, CancellationToken ct = default)
    {
        if (!_hasKey) return "chitchat"; // safe default for router

        var payload = new
        {
            model = _model,
            messages = new[] {
                new { role = "system", content = system },
                new { role = "user",   content = user }
            },
            temperature = 0.0
        };

        using var resp = await _http.PostAsJsonAsync("chat/completions", payload, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) return "chitchat";

        using var doc = System.Text.Json.JsonDocument.Parse(text);
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "chitchat";
    }
}
