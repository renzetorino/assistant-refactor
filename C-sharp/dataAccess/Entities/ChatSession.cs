using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace dataAccess.Entities;

/// <summary>
/// Represents a chat session/conversation between user and AI assistant
/// </summary>
[Table("chat_sessions")]
public class ChatSession
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [Column("user_id")]
    public Guid UserId { get; set; }

    [Column("started_at")]
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    [Column("last_activity_at")]
    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;

    [Column("expires_at")]
    public DateTime ExpiresAt { get; set; }

    [Column("message_count")]
    public int MessageCount { get; set; }

    [Column("metadata", TypeName = "jsonb")]
    public string? Metadata { get; set; }

    /// <summary>
    /// Stores serialized PlannerResultV2 JSON when waiting for user clarification
    /// </summary>
    [Column("pending_plan_json", TypeName = "jsonb")]
    public string? PendingPlanJson { get; set; }

    /// <summary>
    /// Stores the name of the slot we are waiting for (e.g., "sub_intent", "date_range")
    /// </summary>
    [Column("pending_slot_name")]
    public string? PendingSlotName { get; set; }

    // Navigation property for messages in this session
    public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();

    // Navigation property for feedback in this session
    public ICollection<ChatFeedback> Feedbacks { get; set; } = new List<ChatFeedback>();
}
