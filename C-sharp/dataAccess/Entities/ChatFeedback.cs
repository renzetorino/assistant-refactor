using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace dataAccess.Entities;

/// <summary>
/// Represents user feedback for a specific chat message
/// </summary>
[Table("chat_feedback")]
public class ChatFeedback
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [Column("message_id")]
    public Guid MessageId { get; set; }

    [Required]
    [Column("session_id")]
    public Guid SessionId { get; set; }

    [Required]
    [Column("user_id")]
    public Guid UserId { get; set; }

    [Required]
    [MaxLength(50)]
    [Column("feedback_type")]
    public string FeedbackType { get; set; } = string.Empty; // 'thumbs_up', 'thumbs_down', 'rating'

    [Column("rating")]
    public int? Rating { get; set; } // 1-5 scale (nullable)

    [Column("comment")]
    public string? Comment { get; set; }

    // Database does NOT have metadata column - removed
    // [Column("metadata", TypeName = "jsonb")]
    // public string? Metadata { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation property to parent session
    [ForeignKey("SessionId")]
    public ChatSession? Session { get; set; }

    // Navigation property to related message
    [ForeignKey("MessageId")]
    public ChatMessage? Message { get; set; }
}
