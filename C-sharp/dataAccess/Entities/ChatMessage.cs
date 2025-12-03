using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace dataAccess.Entities;

/// <summary>
/// Represents a single message exchange (user query + AI response) with full telemetry
/// </summary>
[Table("chat_messages")]
public class ChatMessage
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [Column("session_id")]
    public Guid SessionId { get; set; }

    [Required]
    [MaxLength(50)]
    [Column("role")]
    public string Role { get; set; } = string.Empty; // 'user' or 'assistant'

    [Required]
    [Column("content")]
    public string Content { get; set; } = string.Empty;

    [MaxLength(100)]
    [Column("intent")]
    public string? Intent { get; set; }

    [MaxLength(100)]
    [Column("domain")]
    public string? Domain { get; set; }

    [Column("confidence")]
    public decimal? Confidence { get; set; }

    [Column("sql_generated")]
    public string? SqlGenerated { get; set; }

    [Column("sql_validated")]
    public bool? SqlValidated { get; set; }

    [Column("sql_executed")]
    public bool? SqlExecuted { get; set; }

    // Database has "result_rows" (integer), not "result_count"
    [Column("result_rows")]
    public int? ResultRows { get; set; }

    // Database has "result_summary" (jsonb)
    [Column("result_summary", TypeName = "jsonb")]
    public string? ResultSummary { get; set; }

    // Database has "analysis" (text)
    [Column("analysis")]
    public string? Analysis { get; set; }

    [Column("latency_ms")]
    public int? LatencyMs { get; set; }

    // Database has "model_name" (text), not "model_used"
    [MaxLength(100)]
    [Column("model_name")]
    public string? ModelName { get; set; }

    [Column("error_message")]
    public string? ErrorMessage { get; set; }

    // Database does NOT have metadata column - removed
    // [Column("metadata", TypeName = "jsonb")]
    // public string? Metadata { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Phase 2: Context Inheritance - Store slots for conversational memory
    /// <summary>
    /// Stores the slot values (e.g., period_start, period_end) extracted/used in this turn.
    /// This enables context inheritance in follow-up queries.
    /// Example: { "period_start": "2025-10-01", "period_end": "2025-10-31" }
    /// </summary>
    [Column("slots", TypeName = "jsonb")]
    public string? SlotsJson { get; set; }

    /// <summary>
    /// Transient property for easy access to slots as a dictionary.
    /// Not mapped to database - use SlotsJson for persistence.
    /// </summary>
    [NotMapped]
    public Dictionary<string, string>? Slots
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SlotsJson))
                return null;
            
            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, string>>(SlotsJson);
            }
            catch
            {
                return null;
            }
        }
        set
        {
            SlotsJson = value == null ? null : JsonSerializer.Serialize(value);
        }
    }

    // Navigation property to parent session
    [ForeignKey("SessionId")]
    public ChatSession? Session { get; set; }
}
