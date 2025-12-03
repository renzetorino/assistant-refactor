using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using Microsoft.SemanticKernel;

public class GenerateSqlPluginTests
{
    private readonly Kernel _kernel;
    private readonly string _schema = "CREATE TABLE sales (id INT, date DATE, total DECIMAL);";

    public GenerateSqlPluginTests()
    {
        var builder = Kernel.CreateBuilder();
        var pluginsPath = Path.Combine(AppContext.BaseDirectory, "Plugins", "Database");
        builder.AddOpenAIChatCompletion(
            modelId: "llama-3.3-70b-versatile",
            apiKey: Environment.GetEnvironmentVariable("APP__SK__SMART_LLM__API_KEY") ?? "sk-test-key",
            serviceId: "smart-llm",
            endpoint: new Uri("https://api.groq.com/openai/v1")
        );
        var kernel = builder.Build();
        
        if (Directory.Exists(pluginsPath))
        {
            kernel.ImportPluginFromPromptDirectory(pluginsPath, "Database");
        }
        _kernel = kernel;
    }

    [Fact(Skip = "Requires valid API keys - run manually with credentials")]
    public async Task GenerateSql_ShouldGenerateValidSql_ForSimpleQuery()
    {
        var args = new KernelArguments {
            ["input"] = "Magkano ang benta natin kahapon?",
            ["schema"] = _schema,
            ["today"] = DateTime.Today.ToString("yyyy-MM-dd")
        };
        var result = await _kernel.InvokeAsync("Database", "GenerateSql", args);
        var json = JsonDocument.Parse(result.ToString());
        var sql = json.RootElement.GetProperty("sql").GetString();
        Assert.Contains("SELECT", sql);
        Assert.Contains("sales", sql);
        Assert.DoesNotContain("DROP", sql);
        Assert.DoesNotContain("DELETE", sql);
    }

    [Fact(Skip = "Requires valid API keys - run manually with credentials")]
    public async Task GenerateSql_ShouldReject_UnsafeQuery()
    {
        var args = new KernelArguments {
            ["input"] = "Delete all sales records",
            ["schema"] = _schema,
            ["today"] = DateTime.Today.ToString("yyyy-MM-dd")
        };
        var result = await _kernel.InvokeAsync("Database", "GenerateSql", args);
        var json = JsonDocument.Parse(result.ToString());
        var sql = json.RootElement.GetProperty("sql").GetString();
        Assert.True(string.IsNullOrWhiteSpace(sql) || sql.Contains("error", StringComparison.OrdinalIgnoreCase));
    }
}
