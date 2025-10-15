using dataAccess.Services;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace dataAccess.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FaqController : ControllerBase
{
    private readonly IVertexAISearchService _faqService;
    private readonly ILogger<FaqController> _logger;

    public FaqController(
        IVertexAISearchService faqService,
        ILogger<FaqController> logger)
    {
        _faqService = faqService;
        _logger = logger;
    }

    /// <summary>
    /// Search FAQ using Vertex AI RAG
    /// </summary>
    /// <param name="request">FAQ search request</param>
    /// <returns>FAQ answer with sources and confidence</returns>
    [HttpPost("search")]
    [ProducesResponseType(typeof(FaqSearchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> SearchFaq([FromBody] FaqSearchRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            var result = await _faqService.SearchFaqAsync(
                request.Query,
                request.UserId,
                request.MaxResults ?? 3
            );

            var response = new FaqSearchResponse
            {
                Query = request.Query,
                Answer = result.Answer,
                Confidence = result.Confidence,
                Sources = result.Chunks.Select(c => new FaqSource
                {
                    Content = c.Content,
                    PageNumber = c.PageNumber,
                    Score = c.Score
                }).ToList(),
                Timestamp = DateTime.UtcNow
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching FAQ for query: {Query}", request.Query);
            return StatusCode(500, new
            {
                error = "An error occurred while searching the FAQ.",
                message = ex.Message
            });
        }
    }

    /// <summary>
    /// Get FAQ search analytics
    /// </summary>
    /// <param name="userId">Optional user ID to filter logs</param>
    /// <param name="limit">Maximum number of logs to return</param>
    /// <returns>Recent FAQ searches</returns>
    [HttpGet("logs")]
    [ProducesResponseType(typeof(List<FaqLogResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLogs(
        [FromQuery] string? userId = null,
        [FromQuery] int limit = 10)
    {
        try
        {
            // This would require adding a method to the service
            // For now, return a placeholder
            return Ok(new List<FaqLogResponse>());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving FAQ logs");
            return StatusCode(500, new { error = "An error occurred while retrieving logs." });
        }
    }
}

// Request/Response DTOs
public class FaqSearchRequest
{
    [Required(ErrorMessage = "Query is required")]
    [MinLength(3, ErrorMessage = "Query must be at least 3 characters")]
    public string Query { get; set; } = string.Empty;

    public string? UserId { get; set; }

    [Range(1, 10, ErrorMessage = "MaxResults must be between 1 and 10")]
    public int? MaxResults { get; set; } = 3;
}

public class FaqSearchResponse
{
    public string Query { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public List<FaqSource> Sources { get; set; } = new();
    public DateTime Timestamp { get; set; }
}

public class FaqSource
{
    public string Content { get; set; } = string.Empty;
    public string PageNumber { get; set; } = string.Empty;
    public double Score { get; set; }
}

public class FaqLogResponse
{
    public long Id { get; set; }
    public string Query { get; set; } = string.Empty;
    public string AnswerSnippet { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public bool? Helpful { get; set; }
    public DateTime CreatedAt { get; set; }
}
