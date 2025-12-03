namespace dataAccess.Api.Services;

/// <summary>
/// Validates Semantic Kernel plugins at application startup.
/// Ensures all required plugin files (skprompt.txt, config.json) exist and are valid.
/// </summary>
public interface IPluginValidator
{
    /// <summary>
    /// Validates all plugins in the Plugins directory.
    /// </summary>
    /// <returns>Validation result with errors and warnings</returns>
    Task<PluginValidationResult> ValidateAllPluginsAsync();
}

public class PluginValidator : IPluginValidator
{
    private readonly ILogger<PluginValidator> _logger;
    private readonly string _pluginsPath;

    public PluginValidator(ILogger<PluginValidator> logger, IWebHostEnvironment environment)
    {
        _logger = logger;
        _pluginsPath = Path.Combine(environment.ContentRootPath, "Plugins");
    }

    public async Task<PluginValidationResult> ValidateAllPluginsAsync()
    {
        var result = new PluginValidationResult();

        if (!Directory.Exists(_pluginsPath))
        {
            result.Errors.Add($"Plugins directory not found: {_pluginsPath}");
            return result;
        }

        var pluginDirs = Directory.GetDirectories(_pluginsPath, "*", SearchOption.AllDirectories)
            .Where(d => Directory.GetFiles(d, "skprompt.txt").Length > 0 
                     || Directory.GetFiles(d, "config.json").Length > 0)
            .ToList();

        if (pluginDirs.Count == 0)
        {
            result.Warnings.Add("No plugin directories found with skprompt.txt or config.json files");
            return result;
        }

        _logger.LogInformation("Validating {Count} plugin directories", pluginDirs.Count);

        foreach (var pluginDir in pluginDirs)
        {
            var pluginName = Path.GetRelativePath(_pluginsPath, pluginDir);
            await ValidatePluginAsync(pluginDir, pluginName, result);
        }

        if (result.IsValid)
        {
            _logger.LogInformation("✅ All {Count} plugins validated successfully", pluginDirs.Count);
        }
        else
        {
            _logger.LogError("❌ Plugin validation failed with {ErrorCount} errors and {WarningCount} warnings", 
                result.Errors.Count, result.Warnings.Count);
        }

        return result;
    }

    private async Task ValidatePluginAsync(string pluginDir, string pluginName, PluginValidationResult result)
    {
        var promptFile = Path.Combine(pluginDir, "skprompt.txt");
        var configFile = Path.Combine(pluginDir, "config.json");

        // Check for required files
        if (!File.Exists(promptFile))
        {
            result.Errors.Add($"Plugin '{pluginName}': Missing skprompt.txt");
            return;
        }

        if (!File.Exists(configFile))
        {
            result.Errors.Add($"Plugin '{pluginName}': Missing config.json");
            return;
        }

        // Validate prompt file
        try
        {
            var promptContent = await File.ReadAllTextAsync(promptFile);
            if (string.IsNullOrWhiteSpace(promptContent))
            {
                result.Errors.Add($"Plugin '{pluginName}': skprompt.txt is empty");
                return;
            }

            // Check for required placeholders (basic validation)
            if (!promptContent.Contains("{{") && !promptContent.Contains("}}"))
            {
                result.Warnings.Add($"Plugin '{pluginName}': skprompt.txt contains no placeholders ({{{{$input}}}}, etc.). This may be intentional.");
            }

            _logger.LogDebug("✓ Plugin '{PluginName}': skprompt.txt validated ({Length} chars)", 
                pluginName, promptContent.Length);
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Plugin '{pluginName}': Error reading skprompt.txt - {ex.Message}");
            return;
        }

        // Validate config file
        try
        {
            var configContent = await File.ReadAllTextAsync(configFile);
            if (string.IsNullOrWhiteSpace(configContent))
            {
                result.Errors.Add($"Plugin '{pluginName}': config.json is empty");
                return;
            }

            // Parse JSON
            using var doc = System.Text.Json.JsonDocument.Parse(configContent);
            var root = doc.RootElement;

            // Validate required fields
            if (!root.TryGetProperty("schema", out _))
            {
                result.Warnings.Add($"Plugin '{pluginName}': config.json missing 'schema' field");
            }

            if (!root.TryGetProperty("type", out var typeEl) || typeEl.GetString() != "completion")
            {
                result.Warnings.Add($"Plugin '{pluginName}': config.json 'type' should be 'completion'");
            }

            if (!root.TryGetProperty("execution_settings", out var execSettings) || 
                execSettings.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                result.Errors.Add($"Plugin '{pluginName}': config.json missing or invalid 'execution_settings'");
                return;
            }

            // Validate execution settings
            if (execSettings.TryGetProperty("default", out var defaultSettings) ||
                execSettings.TryGetProperty("fast-llm", out defaultSettings) ||
                execSettings.TryGetProperty("smart-llm", out defaultSettings))
            {
                if (!defaultSettings.TryGetProperty("model_id", out _))
                {
                    result.Errors.Add($"Plugin '{pluginName}': config.json missing 'execution_settings.*.model_id'");
                }

                // Note: service_id is optional when using the service name as the key
                if (defaultSettings.TryGetProperty("service_id", out var serviceId))
                {
                    var sid = serviceId.GetString();
                    if (sid != "fast-llm" && sid != "smart-llm")
                    {
                        result.Warnings.Add($"Plugin '{pluginName}': service_id '{sid}' is not 'fast-llm' or 'smart-llm'. Ensure it's registered in SK Kernel.");
                    }
                }

                if (!defaultSettings.TryGetProperty("temperature", out _))
                {
                    result.Warnings.Add($"Plugin '{pluginName}': config.json missing 'temperature' setting");
                }

                if (!defaultSettings.TryGetProperty("max_tokens", out _))
                {
                    result.Warnings.Add($"Plugin '{pluginName}': config.json missing 'max_tokens' setting");
                }
            }
            else
            {
                result.Errors.Add($"Plugin '{pluginName}': config.json missing execution settings (expected 'default', 'fast-llm', or 'smart-llm' key)");
            }

            _logger.LogDebug("✓ Plugin '{PluginName}': config.json validated", pluginName);
        }
        catch (System.Text.Json.JsonException ex)
        {
            result.Errors.Add($"Plugin '{pluginName}': Invalid JSON in config.json - {ex.Message}");
            return;
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Plugin '{pluginName}': Error reading config.json - {ex.Message}");
            return;
        }

        result.ValidPlugins.Add(pluginName);
    }
}

public class PluginValidationResult
{
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
    public List<string> ValidPlugins { get; } = new();

    public bool IsValid => Errors.Count == 0;
}
