using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using Microsoft.SemanticKernel;

public class SummarizeResultsPluginTests
{
    private readonly Kernel _kernel;

    public SummarizeResultsPluginTests()
    {
        var builder = Kernel.CreateBuilder();
        var pluginsPath = Path.Combine(AppContext.BaseDirectory, "Plugins", "Analysis");
        builder.AddOpenAIChatCompletion(
            modelId: "llama-3.3-70b-versatile",
            apiKey: Environment.GetEnvironmentVariable("APP__SK__SMART_LLM__API_KEY") ?? "sk-test-key",
            serviceId: "smart-llm",
            endpoint: new Uri("https://api.groq.com/openai/v1")
        );
        var kernel = builder.Build();
        
        if (Directory.Exists(pluginsPath))
        {
            kernel.ImportPluginFromPromptDirectory(pluginsPath, "Analysis");
        }
        _kernel = kernel;
    }

    [Fact(Skip = "Requires valid API keys - run manually with credentials")]
    public async Task SummarizeResults_ShouldFormat_Currency()
    {
        var args = new KernelArguments {
            ["input"] = "Magkano ang benta?",
            ["data"] = "[{\"total\": 12345.67}]"
        };
        var result = await _kernel.InvokeAsync("Analysis", "SummarizeResults", args);
        var summary = result.ToString();
        Assert.Contains("₱", summary);
        Assert.Contains("12,345", summary);
    }

    [Fact(Skip = "Requires valid API keys - run manually with credentials")]
    public async Task SummarizeResults_ShouldHandle_EmptyResults()
    {
        var args = new KernelArguments {
            ["input"] = "Magkano ang benta?",
            ["data"] = "[]"
        };
        var result = await _kernel.InvokeAsync("Analysis", "SummarizeResults", args);
        var summary = result.ToString().ToLower();
        Assert.Contains("walang data", summary);
    }
}
