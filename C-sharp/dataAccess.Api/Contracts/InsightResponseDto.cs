using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace dataAccess.Api.Contracts;

/// <summary>
/// Response DTO for mentor insight data.
/// </summary>
public sealed class InsightResponseDto
{
    /// <summary>
    /// Unique identifier for the insight.
    /// </summary>
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    /// <summary>
    /// Business category (inventory, finance, sales).
    /// </summary>
    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// AI-generated mentor message in Taglish.
    /// </summary>
    [JsonPropertyName("mentorMessage")]
    public string MentorMessage { get; set; } = string.Empty;

    /// <summary>
    /// Detector version used to generate this insight.
    /// </summary>
    [JsonPropertyName("detectorVersion")]
    public string DetectorVersion { get; set; } = string.Empty;

    /// <summary>
    /// When this insight was generated (UTC).
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Source event that triggered this insight generation (e.g., "manual", "scheduled", "api-refresh").
    /// </summary>
    [JsonPropertyName("sourceEvent")]
    public string SourceEvent { get; set; } = string.Empty;

    /// <summary>
    /// Number of findings in this insight.
    /// </summary>
    [JsonPropertyName("findingCount")]
    public int FindingCount { get; set; }
}

/// <summary>
/// Response wrapper for insight operations.
/// </summary>
public sealed class InsightsOperationResponse
{
    /// <summary>
    /// List of insights returned.
    /// </summary>
    [JsonPropertyName("insights")]
    public List<InsightResponseDto> Insights { get; set; } = new();

    /// <summary>
    /// Whether the response came from cache.
    /// </summary>
    [JsonPropertyName("cacheHit")]
    public bool CacheHit { get; set; }

    /// <summary>
    /// Cache age in minutes (if cache hit).
    /// </summary>
    [JsonPropertyName("cacheAgeMinutes")]
    public int? CacheAgeMinutes { get; set; }

    /// <summary>
    /// Total execution time in milliseconds.
    /// </summary>
    [JsonPropertyName("executionTimeMs")]
    public long ExecutionTimeMs { get; set; }

    /// <summary>
    /// Detector version used.
    /// </summary>
    [JsonPropertyName("detectorVersion")]
    public string DetectorVersion { get; set; } = string.Empty;

    /// <summary>
    /// Any warning or informational message.
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
