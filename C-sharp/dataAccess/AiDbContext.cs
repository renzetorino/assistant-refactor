using dataAccess.Entities;
using Microsoft.EntityFrameworkCore;

namespace dataAccess.Services
{
    /// <summary>
    /// DbContext for AI-related features (chat, feedback, forecasts, reports).
    /// Uses the VEC connection string (APP__VEC__CONNECTIONSTRING).
    /// </summary>
    public class AiDbContext : DbContext
    {
        public AiDbContext(DbContextOptions<AiDbContext> options)
            : base(options) { }

        // --- AI Chat System ---
        public DbSet<ChatSession> ChatSessions { get; set; } = default!;
        public DbSet<ChatMessage> ChatMessages { get; set; } = default!;
        public DbSet<ChatFeedback> ChatFeedbacks { get; set; } = default!;

        // --- FAQ Search Logs ---
        public DbSet<FaqSearchLog> FaqSearchLogs { get; set; } = default!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Default schema
            modelBuilder.HasDefaultSchema("public");

            // =========================
            // FAQ SEARCH LOGS
            // =========================
            modelBuilder.Entity<FaqSearchLog>(e =>
            {
                e.ToTable("faq_search_logs");
                e.HasKey(x => x.Id);

                e.Property(x => x.Id).HasColumnName("id");
                e.Property(x => x.UserId).HasColumnName("user_id");
                e.Property(x => x.Query).HasColumnName("query");
                e.Property(x => x.Intent).HasColumnName("intent");
                e.Property(x => x.AnswerSnippet).HasColumnName("answer_snippet");
                e.Property(x => x.Confidence).HasColumnName("confidence");
                e.Property(x => x.Helpful).HasColumnName("helpful");
                e.Property(x => x.CreatedAt).HasColumnName("created_at");
            });

            // =========================
            // AI CHAT SYSTEM
            // =========================
            modelBuilder.Entity<ChatSession>(e =>
            {
                e.ToTable("chat_sessions");
                e.HasKey(x => x.Id);

                e.Property(x => x.Id).HasColumnName("id");
                e.Property(x => x.UserId).HasColumnName("user_id");
                e.Property(x => x.StartedAt).HasColumnName("started_at");
                e.Property(x => x.LastActivityAt).HasColumnName("last_activity_at");
                e.Property(x => x.ExpiresAt).HasColumnName("expires_at");
                e.Property(x => x.MessageCount).HasColumnName("message_count");
                e.Property(x => x.Metadata).HasColumnName("metadata").HasColumnType("jsonb");

                e.HasIndex(x => x.UserId).HasDatabaseName("idx_chat_sessions_user_id");
                e.HasIndex(x => x.ExpiresAt).HasDatabaseName("idx_chat_sessions_expires_at");

                e.HasMany(x => x.Messages)
                    .WithOne(m => m.Session)
                    .HasForeignKey(m => m.SessionId)
                    .OnDelete(DeleteBehavior.Cascade);

                e.HasMany(x => x.Feedbacks)
                    .WithOne(f => f.Session)
                    .HasForeignKey(f => f.SessionId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ChatMessage>(e =>
            {
                e.ToTable("chat_messages");
                e.HasKey(x => x.Id);

                e.Property(x => x.Id).HasColumnName("id");
                e.Property(x => x.SessionId).HasColumnName("session_id");
                e.Property(x => x.Role).HasColumnName("role");
                e.Property(x => x.Content).HasColumnName("content");
                e.Property(x => x.Intent).HasColumnName("intent");
                e.Property(x => x.Domain).HasColumnName("domain");
                e.Property(x => x.Confidence).HasColumnName("confidence");
                e.Property(x => x.SqlGenerated).HasColumnName("sql_generated");
                e.Property(x => x.SqlValidated).HasColumnName("sql_validated");
                e.Property(x => x.SqlExecuted).HasColumnName("sql_executed");
                e.Property(x => x.ResultRows).HasColumnName("result_rows");
                e.Property(x => x.ResultSummary).HasColumnName("result_summary").HasColumnType("jsonb");
                e.Property(x => x.Analysis).HasColumnName("analysis");
                e.Property(x => x.LatencyMs).HasColumnName("latency_ms");
                e.Property(x => x.ModelName).HasColumnName("model_name");
                e.Property(x => x.ErrorMessage).HasColumnName("error_message");
                e.Property(x => x.CreatedAt).HasColumnName("created_at");

                e.HasIndex(x => x.SessionId).HasDatabaseName("idx_chat_messages_session_id");
                e.HasIndex(x => x.Intent).HasDatabaseName("idx_chat_messages_intent");
                e.HasIndex(x => x.CreatedAt).HasDatabaseName("idx_chat_messages_created_at");
            });

            modelBuilder.Entity<ChatFeedback>(e =>
            {
                e.ToTable("chat_feedback");
                e.HasKey(x => x.Id);

                e.Property(x => x.Id).HasColumnName("id");
                e.Property(x => x.MessageId).HasColumnName("message_id");
                e.Property(x => x.SessionId).HasColumnName("session_id");
                e.Property(x => x.UserId).HasColumnName("user_id");
                e.Property(x => x.FeedbackType).HasColumnName("feedback_type");
                e.Property(x => x.Rating).HasColumnName("rating");
                e.Property(x => x.Comment).HasColumnName("comment");
                e.Property(x => x.CreatedAt).HasColumnName("created_at");

                e.HasIndex(x => x.MessageId).HasDatabaseName("idx_chat_feedback_message_id");
                e.HasIndex(x => x.SessionId).HasDatabaseName("idx_chat_feedback_session_id");
                e.HasIndex(x => x.UserId).HasDatabaseName("idx_chat_feedback_user_id");
                e.HasIndex(x => x.FeedbackType).HasDatabaseName("idx_chat_feedback_feedback_type");

                e.HasOne(x => x.Message)
                    .WithMany()
                    .HasForeignKey(x => x.MessageId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
