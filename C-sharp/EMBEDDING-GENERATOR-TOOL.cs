// ====================================================================
// EMBEDDING GENERATOR TOOL
// Purpose: Generate embeddings for all intent classification examples
// ====================================================================
// This is a standalone console application that reads intent.examples.raw.json
// (examples WITHOUT embeddings) and outputs intent.examples.json (WITH embeddings).
//
// USAGE:
// 1. Create a new console project:
//    dotnet new console -n EmbeddingGenerator
//    cd EmbeddingGenerator
//
// 2. Add project reference:
//    dotnet add reference ../dataAccess/dataAccess.csproj
//
// 3. Copy this code into Program.cs
//
// 4. Run:
//    dotnet run
// ====================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using dataAccess.Services;
using Microsoft.Extensions.Logging.Abstractions;

Console.WriteLine("=== Intent Examples Embedding Generator ===\n");

// Paths (adjust as needed)
var inputPath = Path.Combine("..", "..", "..", "..", "dataAccess.Api", "Planning", "Prompts", "intent.examples.raw.json");
var outputPath = inputPath.Replace(".raw.json", ".json");

if (!File.Exists(inputPath))
{
    Console.WriteLine($"ERROR: Input file not found: {inputPath}");
    Console.WriteLine("\nPlease create intent.examples.raw.json with this structure:");
    Console.WriteLine(@"{
  ""metadata"": {
    ""version"": ""1.0"",
    ""embeddingModel"": """",
    ""embeddingDimensions"": 0,
    ""lastUpdated"": ""2025-11-17T00:00:00Z""
  },
  ""examples"": [
    {
      ""id"": 1,
      ""input"": ""yo"",
      ""intent"": ""chitchat"",
      ""domain"": null,
      ""confidence"": 0.95,
      ""tags"": [""greeting""],
      ""context"": null,
      ""embedding"": []
    }
  ]
}");
    return;
}

// Load examples
var json = await File.ReadAllTextAsync(inputPath);
var data = JsonSerializer.Deserialize<IntentExamplesData>(json, new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true
});

if (data == null || data.Examples == null || data.Examples.Count == 0)
{
    Console.WriteLine("ERROR: No examples found in input file.");
    return;
}

Console.WriteLine($"Loaded {data.Examples.Count} examples from {Path.GetFileName(inputPath)}\n");

// Initialize embedding service
Console.WriteLine("Initializing embedding service (ONNX + WordPiece tokenizer)...");
using var embeddingService = new LocalEmbeddingService(new NullLogger<LocalEmbeddingService>());
Console.WriteLine("✓ Embedding service ready\n");

// Generate embeddings
Console.WriteLine("Generating embeddings...\n");
var startTime = DateTime.UtcNow;
int processed = 0;
int failed = 0;

foreach (var example in data.Examples)
{
    try
    {
        var progress = $"[{++processed}/{data.Examples.Count}]";
        Console.Write($"{progress} Processing: \"{example.Input}\"... ");
        
        example.Embedding = await embeddingService.GetEmbeddingAsync(example.Input);
        
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("✓");
        Console.ResetColor();
    }
    catch (Exception ex)
    {
        failed++;
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"✗ ERROR: {ex.Message}");
        Console.ResetColor();
    }
}

var elapsed = DateTime.UtcNow - startTime;

// Update metadata
data.Metadata ??= new IntentMetadata();
data.Metadata.LastUpdated = DateTime.UtcNow;
data.Metadata.EmbeddingModel = "all-MiniLM-L6-v2";
data.Metadata.EmbeddingDimensions = 384;
data.Metadata.Version = "1.0";

// Save output
var options = new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
};

var outputJson = JsonSerializer.Serialize(data, options);
await File.WriteAllTextAsync(outputPath, outputJson);

// Summary
Console.WriteLine("\n" + new string('=', 60));
Console.WriteLine("SUMMARY");
Console.WriteLine(new string('=', 60));
Console.WriteLine($"✓ Total examples: {data.Examples.Count}");
Console.WriteLine($"✓ Successfully processed: {processed - failed}");
if (failed > 0)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"⚠ Failed: {failed}");
    Console.ResetColor();
}
Console.WriteLine($"✓ Time elapsed: {elapsed.TotalSeconds:F1}s");
Console.WriteLine($"✓ Average: {elapsed.TotalMilliseconds / processed:F0}ms per example");
Console.WriteLine($"\n✓ Output saved to: {outputPath}");
Console.WriteLine($"  File size: {new FileInfo(outputPath).Length / 1024}KB");
Console.WriteLine(new string('=', 60));

Console.WriteLine("\n✅ Embedding generation complete!");
Console.WriteLine("\nNext steps:");
Console.WriteLine("1. Copy intent.examples.json to dataAccess.Api/Planning/Prompts/ folder");
Console.WriteLine("2. Restart your API server");
Console.WriteLine("3. Test with: POST /api/assistant { \"text\": \"yo\" }");

// ====================================================================
// MODEL CLASSES (must match IntentExampleRetriever.cs)
// ====================================================================

public class IntentExamplesData
{
    public IntentMetadata? Metadata { get; set; }
    public List<IntentExample> Examples { get; set; } = new();
}

public class IntentMetadata
{
    public string Version { get; set; } = "";
    public string EmbeddingModel { get; set; } = "";
    public int EmbeddingDimensions { get; set; }
    public DateTime LastUpdated { get; set; }
}

public class IntentExample
{
    public int Id { get; set; }
    public string Input { get; set; } = "";
    public string Intent { get; set; } = "";
    public string? Domain { get; set; }
    public float Confidence { get; set; }
    public List<string> Tags { get; set; } = new();
    public string? Context { get; set; }
    public float[] Embedding { get; set; } = Array.Empty<float>();
}
