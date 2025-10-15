using Microsoft.AspNetCore.Mvc;

namespace dataAccess.Api.Contracts;

public record SqlRequestDto(string QueryId, Dictionary<string, object?> Args);

// Phase-1 input: user prompt
public record ReportPrompt(string Text);

// Phase-2 input: original prompt + SQL requests from Phase-1
public record ExpenseRenderRequest(string Text, IReadOnlyList<SqlRequestDto> SqlRequests);
