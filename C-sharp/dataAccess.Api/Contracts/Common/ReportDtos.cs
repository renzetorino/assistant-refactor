using Microsoft.AspNetCore.Mvc;

namespace dataAccess.Api.Contracts.Common;

public sealed record ReportPrompt(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    bool CompareToPrior = false,
    int? UserId = null,
    int[]? LabelIds = null,
    int[]? CategoryIds = null,
    decimal? MinAmount = null,
    decimal? MaxAmount = null,
    string? Status = null
);

public sealed record ExpenseRenderRequest(
    ReportPrompt Prompt,
    // Optional: client-side “regenerate but keep numbers” flag
    bool VaryNarrativeStyle = false
);

// Simple pass-through for any ad-hoc SQL request you may expose/test
public sealed record SqlRequestDto(
    string Operation,                // e.g., "EXPENSE_SUMMARY"
    IDictionary<string, string> Args // e.g., { start: "2025-05-01", end: "2025-05-31" }
);
