using System;
using System.IO;
using Xunit;

public class PluginFilesTests
{
    [Fact]
    public void Plugins_OrchestrationRouter_FilesExist()
    {
        var pluginsPath = Path.Combine(AppContext.BaseDirectory, "Plugins", "Orchestration", "Router");
        var promptFile = Path.Combine(pluginsPath, "skprompt.txt");
        var configFile = Path.Combine(pluginsPath, "config.json");
        
        Assert.True(Directory.Exists(pluginsPath), $"Plugin directory not found: {pluginsPath}");
        Assert.True(File.Exists(promptFile), $"Prompt file not found: {promptFile}");
        Assert.True(File.Exists(configFile), $"Config file not found: {configFile}");
    }

    [Fact]
    public void Plugins_DatabaseGenerateSql_FilesExist()
    {
        var pluginsPath = Path.Combine(AppContext.BaseDirectory, "Plugins", "Database", "GenerateSql");
        var promptFile = Path.Combine(pluginsPath, "skprompt.txt");
        var configFile = Path.Combine(pluginsPath, "config.json");
        
        Assert.True(Directory.Exists(pluginsPath), $"Plugin directory not found: {pluginsPath}");
        Assert.True(File.Exists(promptFile), $"Prompt file not found: {promptFile}");
        Assert.True(File.Exists(configFile), $"Config file not found: {configFile}");
    }

    [Fact]
    public void Plugins_AnalysisSummarizeResults_FilesExist()
    {
        var pluginsPath = Path.Combine(AppContext.BaseDirectory, "Plugins", "Analysis", "SummarizeResults");
        var promptFile = Path.Combine(pluginsPath, "skprompt.txt");
        var configFile = Path.Combine(pluginsPath, "config.json");
        
        Assert.True(Directory.Exists(pluginsPath), $"Plugin directory not found: {pluginsPath}");
        Assert.True(File.Exists(promptFile), $"Prompt file not found: {promptFile}");
        Assert.True(File.Exists(configFile), $"Config file not found: {configFile}");
    }
}
