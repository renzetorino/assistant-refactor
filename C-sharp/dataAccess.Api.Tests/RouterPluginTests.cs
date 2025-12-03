using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using Microsoft.SemanticKernel;

public class RouterPluginTests
{
    private readonly Kernel _kernel;

    public RouterPluginTests()
    {
        // Use Plugins directory from test output
        var builder = Kernel.CreateBuilder();
        var pluginsPath = Path.Combine(AppContext.BaseDirectory, "Plugins", "Orchestration");
        builder.AddOpenAIChatCompletion(
            modelId: "llama-3.1-8b-instant",
            apiKey: Environment.GetEnvironmentVariable("APP__SK__FAST_LLM__API_KEY") ?? "sk-test-key",
            serviceId: "fast-llm",
            endpoint: new Uri("https://api.groq.com/openai/v1")
        );
        var kernel = builder.Build();
        
        // Check if plugins directory exists before importing
        if (Directory.Exists(pluginsPath))
        {
            kernel.ImportPluginFromPromptDirectory(pluginsPath, "Orchestration");
        }
        _kernel = kernel;
    }

    [Theory(Skip = "Requires valid API keys - run manually with credentials")]
    [InlineData("Magkano ang benta natin kahapon?", "GetDataQuery")]
    [InlineData("Kamusta ka?", "ChitChat")]
    [InlineData("Ano ang patakaran sa refund?", "BusinessRuleQuery")]
    [InlineData("Paki-ulit ang tanong.", "Clarification")]
    [InlineData("Gusto ko ng pizza.", "OutOfScope")]
    public async Task Router_ShouldClassifyIntent(string query, string expectedIntent)
    {
        var args = new KernelArguments { ["input"] = query };
        var result = await _kernel.InvokeAsync("Orchestration", "Router", args);
        var json = JsonDocument.Parse(result.ToString());
        var intent = json.RootElement.GetProperty("intent").GetString();
        Assert.Equal(expectedIntent, intent);
    }
}
