using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace dataAccess.Api.Services;

/// <summary>
/// Loads and manages LLM SQL prompt templates from YAML configuration.
/// </summary>
public class LlmSqlPromptLoader
{
    private readonly string _yamlPath;
    private LlmSqlPromptConfig? _config;
    private readonly object _lock = new();

    public LlmSqlPromptLoader()
    {
        // Try multiple possible locations for the YAML file
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Planning", "Prompts", "llm.sql.yaml"),
            Path.Combine(Directory.GetCurrentDirectory(), "Planning", "Prompts", "llm.sql.yaml"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Planning", "Prompts", "llm.sql.yaml")
        };

        _yamlPath = candidates.FirstOrDefault(File.Exists)
                    ?? throw new FileNotFoundException("llm.sql.yaml not found in expected locations");
    }

    /// <summary>
    /// Loads the prompt configuration from YAML. Caches result for performance.
    /// </summary>
    public LlmSqlPromptConfig LoadConfig()
    {
        if (_config != null)
            return _config;

        lock (_lock)
        {
            if (_config != null)
                return _config;

            var yaml = File.ReadAllText(_yamlPath);
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            _config = deserializer.Deserialize<LlmSqlPromptConfig>(yaml)
                      ?? throw new InvalidOperationException("Failed to deserialize llm.sql.yaml");

            return _config;
        }
    }

    /// <summary>
    /// Reloads the configuration from disk (useful for hot-reload scenarios).
    /// </summary>
    public void Reload()
    {
        lock (_lock)
        {
            _config = null;
        }
        LoadConfig();
    }
}

/// <summary>
/// Model for the LLM SQL prompt YAML configuration.
/// </summary>
public class LlmSqlPromptConfig
{
    public string SystemPrompt { get; set; } = string.Empty;
    public string UserPrompt { get; set; } = string.Empty;
    public string SchemaTemplate { get; set; } = string.Empty;
    public string RelationshipsTemplate { get; set; } = string.Empty;
}
