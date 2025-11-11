using System;

namespace dataAccess.Entities;

public class FaqSearchLog
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public string Query { get; set; } = string.Empty;
    public string Intent { get; set; } = "faq";
    public string? AnswerSnippet { get; set; }
    public decimal Confidence { get; set; }
    public bool? Helpful { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
