using System;
using System.Collections.Generic;

namespace dataAccess.Planning.Nlq;

/// <summary>
/// Represents a time specification for NLQ queries.
/// </summary>
public sealed record TimeSpec
{
    public string? Preset { get; init; }
    public string? Start { get; init; }
    public string? End { get; init; }
}

/// <summary>
/// Draft plan parsed from user's natural language text.
/// </summary>
public sealed record NlqPlan
{
    public string Mode { get; init; } = "answer";
    public List<string> Domains { get; init; } = new();
    public string? Metric { get; init; }
    public TimeSpec Time { get; init; } = new();
    public Dictionary<string, string> Filters { get; init; } = new();
    public bool CompareToPrior { get; init; }
    public double Confidence { get; init; }
    public Dictionary<string, object?>? Entities { get; init; }

    /// <summary>
    /// Convenience property to get the first domain (most common use case).
    /// </summary>
    public string? Domain => Domains.Count > 0 ? Domains[0] : null;
}

/// <summary>
/// Resolved plan with concrete date ranges.
/// </summary>
public sealed record NlqResolvedPlan
{
    public string Mode { get; init; } = "answer";
    public List<string> Domains { get; init; } = new();
    public string? Metric { get; init; }
    public TimeSpec Time { get; init; } = new();
    public Dictionary<string, string> Filters { get; init; } = new();
    public bool CompareToPrior { get; init; }
    public double Confidence { get; init; }
    
    // Resolved time ranges
    public DateOnly Start { get; init; }
    public DateOnly End { get; init; }
    public DateOnly? PriorStart { get; init; }
    public DateOnly? PriorEnd { get; init; }

    /// <summary>
    /// Convenience property to get the first domain (most common use case).
    /// </summary>
    public string? Domain => Domains.Count > 0 ? Domains[0] : null;
}
