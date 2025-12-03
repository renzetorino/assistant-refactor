using System;
using dataAccess.Entities;
using dataAccess.Services;
using Microsoft.EntityFrameworkCore;

namespace dataAccess.Services;

/// <summary>
/// Service for logging all LLM interactions to database for telemetry and fine-tuning.
/// Uses AiDbContext for chat sessions, messages, and feedback (VEC database).
/// </summary>
public class TelemetryLogger
{
    private const int DefaultSessionTtlMinutes = 30;
    private readonly AiDbContext _context;

    public TelemetryLogger(AiDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Creates a new chat session for a user
    /// </summary>
    public async Task<ChatSession> CreateSessionAsync(Guid userId, int ttlMinutes = 30, CancellationToken ct = default)
    {
        var session = new ChatSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            StartedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(ttlMinutes),
            MessageCount = 0
        };

        _context.ChatSessions.Add(session);
        await _context.SaveChangesAsync(ct);

        return session;
    }

    /// <summary>
    /// Updates the last activity time for a session (extends TTL)
    /// </summary>
    public async Task UpdateSessionActivityAsync(Guid sessionId, int ttlMinutes = 30, bool incrementMessageCount = false, CancellationToken ct = default)
    {
        var session = await LoadSessionAsync(sessionId, ct);
        RefreshSessionActivity(session, ttlMinutes, incrementMessageCount);
        await _context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Logs a user message to the database
    /// </summary>
    public async Task<ChatMessage> LogUserMessageAsync(
        Guid sessionId,
        Guid userId,
        string content,
        CancellationToken ct = default)
    {
        var session = await LoadSessionForUserAsync(sessionId, userId, ct);

        var message = new ChatMessage
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            Role = "user",
            Content = content,
            CreatedAt = DateTime.UtcNow
        };

        _context.ChatMessages.Add(message);
        RefreshSessionActivity(session, DefaultSessionTtlMinutes, incrementMessageCount: true);
        await _context.SaveChangesAsync(ct);

        return message;
    }

    /// <summary>
    /// Logs an assistant (AI) response with full telemetry
    /// </summary>
    public async Task<ChatMessage> LogAssistantMessageAsync(
        Guid sessionId,
        Guid userId,
        string content,
        string? intent = null,
        string? domain = null,
        decimal? confidence = null,
        string? sqlGenerated = null,
        bool? sqlValidated = null,
        bool? sqlExecuted = null,
        int? resultRows = null,
        string? resultSummary = null,
        string? analysis = null,
        int? latencyMs = null,
        string? modelName = null,
        string? errorMessage = null,
        CancellationToken ct = default)
    {
        var session = await LoadSessionForUserAsync(sessionId, userId, ct);

        var message = new ChatMessage
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            Role = "assistant",
            Content = content,
            Intent = intent,
            Domain = domain,
            Confidence = confidence,
            SqlGenerated = sqlGenerated,
            SqlValidated = sqlValidated,
            SqlExecuted = sqlExecuted,
            ResultRows = resultRows,
            ResultSummary = resultSummary,
            Analysis = analysis,
            LatencyMs = latencyMs,
            ModelName = modelName,
            ErrorMessage = errorMessage,
            CreatedAt = DateTime.UtcNow
        };

        _context.ChatMessages.Add(message);
        RefreshSessionActivity(session, DefaultSessionTtlMinutes, incrementMessageCount: true);
        await _context.SaveChangesAsync(ct);

        return message;
    }

    /// <summary>
    /// Creates a placeholder assistant message for streaming scenarios and returns the persisted entity.
    /// </summary>
    public async Task<ChatMessage> CreateAssistantMessagePlaceholderAsync(
        Guid sessionId,
        Guid userId,
        string? intent = null,
        string? modelName = null,
        CancellationToken ct = default)
    {
        var session = await LoadSessionForUserAsync(sessionId, userId, ct);

        var message = new ChatMessage
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            Role = "assistant",
            Content = string.Empty,
            Intent = intent,
            ModelName = modelName,
            CreatedAt = DateTime.UtcNow
        };

        _context.ChatMessages.Add(message);
        RefreshSessionActivity(session, DefaultSessionTtlMinutes, incrementMessageCount: true);
        await _context.SaveChangesAsync(ct);

        return message;
    }

    /// <summary>
    /// Appends streamed content to an assistant message placeholder.
    /// </summary>
    public async Task AppendAssistantChunkAsync(
        Guid messageId,
        string contentChunk,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(contentChunk))
        {
            return;
        }

        var message = await _context.ChatMessages.FirstOrDefaultAsync(m => m.Id == messageId, ct);
        if (message == null)
        {
            throw new InvalidOperationException($"Chat message {messageId} was not found.");
        }

        if (!string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Chat message {messageId} is not an assistant message and cannot accept streaming chunks.");
        }

        message.Content = (message.Content ?? string.Empty) + contentChunk;
        await _context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Finalizes a streaming assistant message with the completed content and telemetry metadata.
    /// </summary>
    public async Task FinalizeAssistantMessageAsync(
        Guid messageId,
        string finalContent,
        string? intent = null,
        string? domain = null,
        decimal? confidence = null,
        string? sqlGenerated = null,
        bool? sqlValidated = null,
        bool? sqlExecuted = null,
        int? resultRows = null,
        string? resultSummary = null,
        string? analysis = null,
        int? latencyMs = null,
        string? modelName = null,
        string? errorMessage = null,
        CancellationToken ct = default)
    {
        var message = await _context.ChatMessages.FirstOrDefaultAsync(m => m.Id == messageId, ct);
        if (message == null)
        {
            throw new InvalidOperationException($"Chat message {messageId} was not found.");
        }

        if (!string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Chat message {messageId} is not an assistant message and cannot be finalized.");
        }

        message.Content = finalContent;
        if (intent != null) message.Intent = intent;
        if (domain != null) message.Domain = domain;
        if (confidence.HasValue) message.Confidence = confidence;
        if (sqlGenerated != null) message.SqlGenerated = sqlGenerated;
        if (sqlValidated.HasValue) message.SqlValidated = sqlValidated;
        if (sqlExecuted.HasValue) message.SqlExecuted = sqlExecuted;
        if (resultRows.HasValue) message.ResultRows = resultRows;
        if (resultSummary != null) message.ResultSummary = resultSummary;
        if (analysis != null) message.Analysis = analysis;
        if (latencyMs.HasValue) message.LatencyMs = latencyMs;
        if (modelName != null) message.ModelName = modelName;
        if (errorMessage != null) message.ErrorMessage = errorMessage;

        var session = await _context.ChatSessions.FirstOrDefaultAsync(s => s.Id == message.SessionId, ct);
        if (session != null)
        {
            RefreshSessionActivity(session, DefaultSessionTtlMinutes, incrementMessageCount: false);
        }

        await _context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Updates an existing message with additional telemetry (e.g., after SQL execution)
    /// </summary>
    public async Task UpdateMessageTelemetryAsync(
        Guid messageId,
        string? sqlGenerated = null,
        bool? sqlValidated = null,
        bool? sqlExecuted = null,
        int? resultRows = null,
        string? resultSummary = null,
        string? analysis = null,
        int? latencyMs = null,
        string? errorMessage = null,
        CancellationToken ct = default)
    {
        var message = await _context.ChatMessages.FindAsync(new object[] { messageId }, ct);
        if (message != null)
        {
            if (sqlGenerated != null) message.SqlGenerated = sqlGenerated;
            if (sqlValidated.HasValue) message.SqlValidated = sqlValidated;
            if (sqlExecuted.HasValue) message.SqlExecuted = sqlExecuted;
            if (resultRows.HasValue) message.ResultRows = resultRows;
            if (resultSummary != null) message.ResultSummary = resultSummary;
            if (analysis != null) message.Analysis = analysis;
            if (latencyMs.HasValue) message.LatencyMs = latencyMs;
            if (errorMessage != null) message.ErrorMessage = errorMessage;

            await _context.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Logs user feedback for a specific message (overload with nullable sessionId)
    /// </summary>
    public async Task<ChatFeedback> LogFeedbackAsync(
        Guid? sessionId,
        Guid messageId,
        Guid userId,
        string feedbackType,
        int? rating = null,
        string? comment = null,
        CancellationToken ct = default)
    {
        // If sessionId is not provided, try to find it from the message
        Guid actualSessionId = sessionId ?? Guid.Empty;
        if (!sessionId.HasValue)
        {
            var message = await _context.ChatMessages.FindAsync(new object[] { messageId }, ct);
            if (message != null)
            {
                actualSessionId = message.SessionId;
            }
        }
        else
        {
            actualSessionId = sessionId.Value;
        }

        var feedback = new ChatFeedback
        {
            Id = Guid.NewGuid(),
            MessageId = messageId,
            SessionId = actualSessionId,
            UserId = userId,
            FeedbackType = feedbackType,
            Rating = rating,
            Comment = comment,
            CreatedAt = DateTime.UtcNow
        };

        _context.ChatFeedbacks.Add(feedback);
        await _context.SaveChangesAsync(ct);

        return feedback;
    }

    /// <summary>
    /// Retrieves conversation history for a session (for context preservation)
    /// </summary>
    public async Task<List<ChatMessage>> GetSessionHistoryAsync(
        Guid sessionId,
        int maxMessages = 20,
        CancellationToken ct = default)
    {
        return await _context.ChatMessages
            .Where(m => m.SessionId == sessionId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(maxMessages)
            .OrderBy(m => m.CreatedAt) // Reverse to chronological order
            .ToListAsync(ct);
    }

    /// <summary>
    /// Retrieves a session by ID (with validation)
    /// </summary>
    public async Task<ChatSession?> GetSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        return await _context.ChatSessions
            .Include(s => s.Messages)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
    }

    /// <summary>
    /// Retrieves the session associated with a specific message.
    /// </summary>
    public async Task<ChatSession?> GetSessionByMessageIdAsync(Guid messageId, CancellationToken ct = default)
    {
        return await _context.ChatMessages
            .Where(m => m.Id == messageId)
            .Select(m => m.Session)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Checks if a session is still valid (not expired)
    /// </summary>
    public async Task<bool> IsSessionValidAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await _context.ChatSessions.FindAsync(new object[] { sessionId }, ct);
        return session != null && session.ExpiresAt > DateTime.UtcNow;
    }

    /// <summary>
    /// Checks if a message exists
    /// </summary>
    public async Task<bool> MessageExistsAsync(Guid messageId, CancellationToken ct = default)
    {
        return await _context.ChatMessages.AnyAsync(m => m.Id == messageId, ct);
    }

    /// <summary>
    /// Cleans up expired sessions (run periodically as background job)
    /// </summary>
    public async Task<int> CleanupExpiredSessionsAsync(CancellationToken ct = default)
    {
        var expiredSessions = await _context.ChatSessions
            .Where(s => s.ExpiresAt < DateTime.UtcNow)
            .ToListAsync(ct);

        _context.ChatSessions.RemoveRange(expiredSessions);
        var count = await _context.SaveChangesAsync(ct);

        return expiredSessions.Count;
    }

    /// <summary>
    /// Gets telemetry statistics for analysis/dashboard
    /// </summary>
    public async Task<Dictionary<string, object>> GetTelemetryStatsAsync(
        DateTime? startDate = null,
        DateTime? endDate = null,
        CancellationToken ct = default)
    {
        startDate ??= DateTime.UtcNow.AddDays(-7);
        endDate ??= DateTime.UtcNow;

        var messages = await _context.ChatMessages
            .Where(m => m.CreatedAt >= startDate && m.CreatedAt <= endDate && m.Role == "assistant")
            .ToListAsync(ct);

        var stats = new Dictionary<string, object>
        {
            ["total_messages"] = messages.Count,
            ["by_intent"] = messages
                .Where(m => m.Intent != null)
                .GroupBy(m => m.Intent)
                .ToDictionary(g => g.Key!, g => g.Count()),
            ["sql_success_rate"] = messages.Count(m => m.SqlExecuted == true) / (double)Math.Max(1, messages.Count(m => m.SqlGenerated != null)),
            ["avg_latency_ms"] = messages.Where(m => m.LatencyMs.HasValue).Average(m => (double?)m.LatencyMs) ?? 0,
            ["error_count"] = messages.Count(m => m.ErrorMessage != null)
        };

        return stats;
    }

    private async Task<ChatSession> LoadSessionAsync(Guid sessionId, CancellationToken ct)
    {
        var session = await _context.ChatSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session == null)
        {
            throw new InvalidOperationException($"Chat session {sessionId} was not found.");
        }

        return session;
    }

    private async Task<ChatSession> LoadSessionForUserAsync(Guid sessionId, Guid userId, CancellationToken ct)
    {
        var session = await LoadSessionAsync(sessionId, ct);
        if (session.UserId != userId)
        {
            throw new InvalidOperationException($"Chat session {sessionId} does not belong to user {userId}.");
        }

        return session;
    }

    private static void RefreshSessionActivity(ChatSession session, int ttlMinutes, bool incrementMessageCount)
    {
        session.LastActivityAt = DateTime.UtcNow;
        session.ExpiresAt = DateTime.UtcNow.AddMinutes(ttlMinutes);

        if (incrementMessageCount)
        {
            session.MessageCount += 1;
        }
    }
}
