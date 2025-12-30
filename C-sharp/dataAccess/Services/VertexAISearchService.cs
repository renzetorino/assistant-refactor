using Google.Cloud.DiscoveryEngine.V1;
using Microsoft.Extensions.Configuration;
using dataAccess.Entities;
using Microsoft.EntityFrameworkCore;

namespace dataAccess.Services;

public interface IVertexAISearchService
{
    Task<FaqSearchResult> SearchFaqAsync(string query, string? userId = null, int maxResults = 3);
}

public record FaqSearchResult(
    string Answer,
    List<FaqChunk> Chunks,
    double Confidence
);

public record FaqChunk(
    string Content,
    string PageNumber,
    double Score
);

public class VertexAISearchService : IVertexAISearchService
{
    private readonly SearchServiceClient _client;
    private readonly string _projectId;
    private readonly string _location;
    private readonly string _dataStoreId;
    private readonly AppDbContext _dbContext;

    public VertexAISearchService(IConfiguration config, AppDbContext dbContext)
    {
        _projectId = config["VertexAI:ProjectId"] 
            ?? Environment.GetEnvironmentVariable("VERTEX_AI_PROJECT_ID")
            ?? throw new ArgumentNullException("VertexAI:ProjectId not configured");
        
        _location = config["VertexAI:Location"] 
            ?? Environment.GetEnvironmentVariable("VERTEX_AI_LOCATION") 
            ?? "global";
        
        _dataStoreId = config["VertexAI:DataStoreId"] 
            ?? Environment.GetEnvironmentVariable("VERTEX_AI_DATASTORE_ID")
            ?? throw new ArgumentNullException("VertexAI:DataStoreId not configured");

        // Resolve credentials path (supports relative path from app base directory)
        var credPath = config["VertexAI:CredentialsPath"] 
            ?? Environment.GetEnvironmentVariable("VERTEX_AI_CREDENTIALS_PATH");
        
        if (!string.IsNullOrEmpty(credPath))
        {
            // Try as absolute path first, then relative to app base directory
            var resolvedPath = File.Exists(credPath) 
                ? credPath 
                : Path.Combine(AppContext.BaseDirectory, credPath);
            
            if (File.Exists(resolvedPath))
            {
                Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", resolvedPath);
                Console.WriteLine($"[VertexAI] Using credentials: {resolvedPath}");
            }
            else
            {
                Console.WriteLine($"[VertexAI] Warning: Credentials file not found at {credPath} or {resolvedPath}");
            }
        }
        
        _client = SearchServiceClient.Create();
        _dbContext = dbContext;
    }

    public async Task<FaqSearchResult> SearchFaqAsync(string query, string? userId = null, int maxResults = 3)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new FaqSearchResult(
                "Please provide a question.",
                new List<FaqChunk>(),
                0.0
            );
        }

        try
        {
            // 1. Search Vertex AI
            var result = await PerformVertexSearchAsync(query, maxResults);
            
            // 2. Log search (if userId provided)
            if (!string.IsNullOrEmpty(userId) && Guid.TryParse(userId, out var userGuid))
            {
                await LogFaqSearchAsync(userGuid, query, result);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VertexAISearchService] Error: {ex.Message}");
            return new FaqSearchResult(
                "Sorry, I encountered an error searching the FAQ. Please try again.",
                new List<FaqChunk>(),
                0.0
            );
        }
    }

    private async Task<FaqSearchResult> PerformVertexSearchAsync(string query, int maxResults)
    {
        var servingConfig = $"projects/{_projectId}/locations/{_location}/" +
            $"collections/default_collection/dataStores/{_dataStoreId}/servingConfigs/default_config";

        var request = new SearchRequest
        {
            ServingConfig = servingConfig,
            Query = query,
            PageSize = maxResults,
            ContentSearchSpec = new SearchRequest.Types.ContentSearchSpec
            {
                ExtractiveContentSpec = new SearchRequest.Types.ContentSearchSpec.Types.ExtractiveContentSpec
                {
                    MaxExtractiveAnswerCount = 1,
                    MaxExtractiveSegmentCount = 3
                },
                SnippetSpec = new SearchRequest.Types.ContentSearchSpec.Types.SnippetSpec
                {
                    ReturnSnippet = true
                }
            }
        };

        var response = _client.SearchAsync(request);
        var results = new List<SearchResponse.Types.SearchResult>();
        
        await foreach (var result in response)
        {
            results.Add(result);
        }

        // No results found - very low confidence
        if (!results.Any())
        {
            Console.WriteLine("[VertexAI] No search results returned");
            return new FaqSearchResult(
                "Sorry, I couldn't find an answer to that question in the FAQ.",
                new List<FaqChunk>(),
                0.0 // Zero confidence = out of scope
            );
        }

        // Extract answer from first result
        var topResult = results.First();
        var answer = ExtractAnswer(topResult);
        
        // Check if we actually got a real answer
        var hasValidAnswer = !string.IsNullOrWhiteSpace(answer) && 
                            answer != "No answer found." &&
                            answer.Length > 10; // Minimum length for valid answer

        // Collect chunks from all results
        var chunks = results.Select((r, index) => new FaqChunk(
            Content: ExtractSnippet(r) ?? "",
            PageNumber: ExtractPageNumber(r),
            Score: 1.0 - (index * 0.1) // Decreasing score based on rank
        )).Where(c => !string.IsNullOrEmpty(c.Content)).ToList();

        // ════════════════════════════════════════════════════════════════
        // PRODUCTION-GRADE CONFIDENCE CALCULATION
        // ════════════════════════════════════════════════════════════════
        // Base confidence on multiple factors:
        // 1. Number of results found (more = higher confidence)
        // 2. Quality of answer extracted (valid answer = boost)
        // 3. Number of chunks with content (more sources = higher confidence)
        // ════════════════════════════════════════════════════════════════
        
        double confidence = 0.0;
        
        // Factor 1: Base confidence from result count
        if (results.Count >= 3)
        {
            confidence = 0.75; // Multiple good matches
        }
        else if (results.Count == 2)
        {
            confidence = 0.65; // Two matches
        }
        else if (results.Count == 1)
        {
            confidence = 0.55; // Single match
        }

        // Factor 2: Boost if we have a valid answer
        if (hasValidAnswer)
        {
            confidence += 0.15;
        }
        else
        {
            // Penalize if no valid answer extracted
            confidence -= 0.30;
        }

        // Factor 3: Boost based on chunk quality
        if (chunks.Count >= 2)
        {
            confidence += 0.10;
        }

        // Clamp confidence between 0 and 1
        confidence = Math.Max(0.0, Math.Min(1.0, confidence));

        Console.WriteLine($"[VertexAI] Results: {results.Count}, Chunks: {chunks.Count}, HasAnswer: {hasValidAnswer}, Confidence: {confidence:F2}");

        return new FaqSearchResult(answer, chunks, confidence);
    }

    private string ExtractAnswer(SearchResponse.Types.SearchResult result)
    {
        if (result.Document?.DerivedStructData?.Fields == null)
            return "No answer found.";

        var fields = result.Document.DerivedStructData.Fields;

        // Try extractive answers first
        if (fields.TryGetValue("extractive_answers", out var answersField))
        {
            var answers = answersField.ListValue?.Values;
            if (answers?.Any() == true)
            {
                var firstAnswer = answers.First().StructValue;
                if (firstAnswer?.Fields.TryGetValue("content", out var content) == true)
                {
                    return content.StringValue ?? "No answer found.";
                }
            }
        }

        // Fallback to snippets
        if (fields.TryGetValue("snippets", out var snippetsField))
        {
            var snippets = snippetsField.ListValue?.Values;
            if (snippets?.Any() == true)
            {
                var firstSnippet = snippets.First().StructValue;
                if (firstSnippet?.Fields.TryGetValue("snippet", out var snippet) == true)
                {
                    return snippet.StringValue ?? "No answer found.";
                }
            }
        }

        return "No answer found.";
    }

    private string? ExtractSnippet(SearchResponse.Types.SearchResult result)
    {
        if (result.Document?.DerivedStructData?.Fields == null)
            return null;

        var fields = result.Document.DerivedStructData.Fields;

        if (fields.TryGetValue("snippets", out var snippetsField))
        {
            var snippets = snippetsField.ListValue?.Values;
            if (snippets?.Any() == true)
            {
                var firstSnippet = snippets.First().StructValue;
                if (firstSnippet?.Fields.TryGetValue("snippet", out var snippet) == true)
                {
                    return snippet.StringValue;
                }
            }
        }

        return null;
    }

    private string ExtractPageNumber(SearchResponse.Types.SearchResult result)
    {
        if (result.Document?.DerivedStructData?.Fields == null)
            return "N/A";

        var fields = result.Document.DerivedStructData.Fields;

        if (fields.TryGetValue("page_number", out var pageField))
        {
            return pageField.StringValue ?? "N/A";
        }

        if (fields.TryGetValue("page_span", out var spanField))
        {
            return spanField.StringValue ?? "N/A";
        }

        return "N/A";
    }

    private async Task LogFaqSearchAsync(Guid userId, string query, FaqSearchResult result)
    {
        try
        {
            var log = new FaqSearchLog
            {
                UserId = userId,
                Query = query,
                Intent = "faq",
                AnswerSnippet = result.Answer.Length > 500 
                    ? result.Answer.Substring(0, 500) + "..." 
                    : result.Answer,
                Confidence = (decimal)result.Confidence,
                CreatedAt = DateTime.UtcNow
            };
            
            _dbContext.Set<FaqSearchLog>().Add(log);
            await _dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Don't fail the FAQ search if logging fails
            Console.WriteLine($"[VertexAISearchService] Failed to log FAQ search: {ex.Message}");
        }
    }
}
