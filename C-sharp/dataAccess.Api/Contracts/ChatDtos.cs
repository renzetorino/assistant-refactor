using System.ComponentModel.DataAnnotations;

namespace dataAccess.Api.Contracts;

/// <summary>
/// Request DTO for chat queries
/// </summary>
public class ChatQueryRequest
{
    /// <summary>
    /// The user's natural language query
    /// </summary>
    [Required(ErrorMessage = "Query is required")]
    [StringLength(2000, MinimumLength = 1, ErrorMessage = "Query must be between 1 and 2000 characters")]
    public string Query { get; set; } = string.Empty;

    /// <summary>
    /// Optional session ID for conversation context.
    /// If not provided, a new session will be created.
    /// </summary>
    public Guid? SessionId { get; set; }

    /// <summary>
    /// Enable streaming response (default: false)
    /// </summary>
    public bool EnableStreaming { get; set; } = false;
}

/// <summary>
/// Response DTO for chat queries (non-streaming)
/// </summary>
public class ChatQueryResponse
{
    /// <summary>
    /// The original user query
    /// </summary>
    public string UserQuery { get; set; } = string.Empty;

    /// <summary>
    /// The AI-generated response
    /// </summary>
    public string Response { get; set; } = string.Empty;

    /// <summary>
    /// Classified intent (e.g., "GetDataQuery", "ChitChat")
    /// </summary>
    public string Intent { get; set; } = string.Empty;

    /// <summary>
    /// Session ID for this conversation
    /// </summary>
    public Guid SessionId { get; set; }

    /// <summary>
    /// User ID
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Whether the query was processed successfully
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// Error message (if any)
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Generated SQL query (if applicable)
    /// </summary>
    public string? GeneratedSql { get; set; }

    /// <summary>
    /// Number of rows returned from the query (if applicable)
    /// </summary>
    public int? ResultCount { get; set; }

    /// <summary>
    /// Total latency in milliseconds
    /// </summary>
    public int TotalLatencyMs { get; set; }

    /// <summary>
    /// Breakdown of latency by pipeline step
    /// </summary>
    public LatencyBreakdown? Latency { get; set; }
}

/// <summary>
/// Detailed latency breakdown for performance monitoring
/// </summary>
public class LatencyBreakdown
{
    public int IntentClassificationMs { get; set; }
    public int SqlGenerationMs { get; set; }
    public int QueryExecutionMs { get; set; }
    public int SummarizationMs { get; set; }
}

/// <summary>
/// Streaming chunk DTO for SSE responses
/// </summary>
public class StreamingChunkDto
{
    /// <summary>
    /// Type of chunk: "metadata", "content", "error", "done"
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Text content (for "content" chunks)
    /// </summary>
    public string? Content { get; set; }

    /// <summary>
    /// Metadata for progress updates (for "metadata" chunks)
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }

    /// <summary>
    /// Error message (for "error" chunks)
    /// </summary>
    public string? Error { get; set; }
}

/// <summary>
/// Request to create a new chat session
/// </summary>
public class CreateSessionRequest
{
    /// <summary>
    /// Session TTL in minutes (default: 30)
    /// </summary>
    [Range(5, 1440, ErrorMessage = "TTL must be between 5 and 1440 minutes (24 hours)")]
    public int TtlMinutes { get; set; } = 30;
}

/// <summary>
/// Response for session creation
/// </summary>
public class CreateSessionResponse
{
    public Guid SessionId { get; set; }
    public Guid UserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}

/// <summary>
/// Request to get conversation history
/// </summary>
public class GetHistoryRequest
{
    /// <summary>
    /// Session ID
    /// </summary>
    [Required(ErrorMessage = "SessionId is required")]
    public Guid SessionId { get; set; }

    /// <summary>
    /// Maximum number of messages to retrieve (default: 20)
    /// </summary>
    [Range(1, 100, ErrorMessage = "MaxMessages must be between 1 and 100")]
    public int MaxMessages { get; set; } = 20;
}

/// <summary>
/// Response for conversation history
/// </summary>
public class GetHistoryResponse
{
    public Guid SessionId { get; set; }
    public List<ChatMessageDto> Messages { get; set; } = new();
}

/// <summary>
/// Chat message DTO
/// </summary>
public class ChatMessageDto
{
    public Guid Id { get; set; }
    public string Role { get; set; } = string.Empty; // "user" or "assistant"
    public string Content { get; set; } = string.Empty;
    public string? Intent { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Request to submit user feedback for a chat message
/// </summary>
public class SubmitFeedbackRequest
{
    /// <summary>
    /// Message ID for which feedback is being submitted
    /// </summary>
    [Required(ErrorMessage = "MessageId is required")]
    public Guid MessageId { get; set; }

    /// <summary>
    /// Session ID (optional, for validation)
    /// </summary>
    public Guid? SessionId { get; set; }

    /// <summary>
    /// Type of feedback: "thumbs_up", "thumbs_down", "report"
    /// </summary>
    [Required(ErrorMessage = "FeedbackType is required")]
    [RegularExpression("^(thumbs_up|thumbs_down|report)$", 
        ErrorMessage = "FeedbackType must be 'thumbs_up', 'thumbs_down', or 'report'")]
    public string FeedbackType { get; set; } = string.Empty;

    /// <summary>
    /// Numeric rating (1-5, optional)
    /// </summary>
    [Range(1, 5, ErrorMessage = "Rating must be between 1 and 5")]
    public int? Rating { get; set; }

    /// <summary>
    /// Free-text comment (optional)
    /// </summary>
    [StringLength(1000, ErrorMessage = "Comment must not exceed 1000 characters")]
    public string? Comment { get; set; }
}

/// <summary>
/// Response for feedback submission
/// </summary>
public class SubmitFeedbackResponse
{
    public Guid FeedbackId { get; set; }
    public Guid MessageId { get; set; }
    public Guid? SessionId { get; set; }
    public string FeedbackType { get; set; } = string.Empty;
    public DateTime SubmittedAt { get; set; }
}
