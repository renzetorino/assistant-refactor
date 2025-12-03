using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using dataAccess.Entities;
using dataAccess.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace dataAccess.Api.Tests;

/// <summary>
/// Unit tests for conversational memory (ChatHistoryService).
/// Tests validate the "array" functionality for maintaining context across turns.
/// </summary>
public class ConversationalMemoryTests
{
    private readonly AiDbContext _context;
    private readonly ChatHistoryService _service;
    private readonly Guid _testSessionId;

    public ConversationalMemoryTests()
    {
        // Setup in-memory database
        var options = new DbContextOptionsBuilder<AiDbContext>()
            .UseInMemoryDatabase($"ConversationalMemoryTests_{Guid.NewGuid()}")
            .Options;
        
        _context = new AiDbContext(options);
        _service = new ChatHistoryService(_context);
        _testSessionId = Guid.NewGuid();
    }

    [Fact]
    public async Task AddMessageToHistoryAsync_Should_Save_User_Message()
    {
        // Arrange
        var sessionId = _testSessionId;
        var role = "user";
        var content = "Show me sales for today";

        // Act
        await _service.AddMessageToHistoryAsync(sessionId, role, content);

        // Assert
        var messages = await _context.ChatMessages
            .Where(m => m.SessionId == sessionId)
            .ToListAsync();

        Assert.Single(messages);
        Assert.Equal(role, messages[0].Role);
        Assert.Equal(content, messages[0].Content);
    }

    [Fact]
    public async Task AddMessageToHistoryAsync_Should_Save_Assistant_Message()
    {
        // Arrange
        var sessionId = _testSessionId;
        var role = "assistant";
        var content = "Sales for today are ₱10,000";

        // Act
        await _service.AddMessageToHistoryAsync(sessionId, role, content);

        // Assert
        var messages = await _context.ChatMessages
            .Where(m => m.SessionId == sessionId)
            .ToListAsync();

        Assert.Single(messages);
        Assert.Equal(role, messages[0].Role);
        Assert.Equal(content, messages[0].Content);
    }

    [Fact]
    public async Task GetRecentMessagesAsync_Should_Return_Empty_List_When_No_Messages()
    {
        // Arrange
        var sessionId = Guid.NewGuid(); // Non-existent session

        // Act
        var messages = await _service.GetRecentMessagesAsync(sessionId);

        // Assert
        Assert.Empty(messages);
    }

    [Fact]
    public async Task GetRecentMessagesAsync_Should_Return_Messages_In_Chronological_Order()
    {
        // Arrange
        var sessionId = _testSessionId;
        await _service.AddMessageToHistoryAsync(sessionId, "user", "First message");
        await Task.Delay(10); // Ensure different timestamps
        await _service.AddMessageToHistoryAsync(sessionId, "assistant", "Second message");
        await Task.Delay(10);
        await _service.AddMessageToHistoryAsync(sessionId, "user", "Third message");

        // Act
        var messages = await _service.GetRecentMessagesAsync(sessionId, limit: 10);

        // Assert
        Assert.Equal(3, messages.Count);
        Assert.Equal("First message", messages[0].Content);
        Assert.Equal("Second message", messages[1].Content);
        Assert.Equal("Third message", messages[2].Content);
    }

    [Fact]
    public async Task GetRecentMessagesAsync_Should_Respect_Limit_Parameter()
    {
        // Arrange
        var sessionId = _testSessionId;
        for (int i = 1; i <= 10; i++)
        {
            await _service.AddMessageToHistoryAsync(sessionId, "user", $"Message {i}");
            await Task.Delay(5);
        }

        // Act
        var messages = await _service.GetRecentMessagesAsync(sessionId, limit: 5);

        // Assert
        Assert.Equal(5, messages.Count);
        // Should return the 5 most recent messages (6, 7, 8, 9, 10)
        Assert.Equal("Message 6", messages[0].Content);
        Assert.Equal("Message 10", messages[4].Content);
    }

    [Fact]
    public async Task GetRecentMessagesAsync_Should_Use_Default_Limit_Of_5()
    {
        // Arrange
        var sessionId = _testSessionId;
        for (int i = 1; i <= 10; i++)
        {
            await _service.AddMessageToHistoryAsync(sessionId, "user", $"Message {i}");
            await Task.Delay(5);
        }

        // Act
        var messages = await _service.GetRecentMessagesAsync(sessionId); // No limit specified

        // Assert
        Assert.Equal(5, messages.Count); // Default limit is 5
    }

    [Fact]
    public async Task ConversationalFlow_Should_Maintain_Context()
    {
        // Arrange - Simulate a 2-turn conversation
        var sessionId = _testSessionId;

        // Turn 1
        await _service.AddMessageToHistoryAsync(sessionId, "user", "Show sales for today");
        await _service.AddMessageToHistoryAsync(sessionId, "assistant", "Sales for today: ₱10,000");

        // Turn 2 - User asks follow-up
        await _service.AddMessageToHistoryAsync(sessionId, "user", "How about inventory?");

        // Act - Retrieve history before processing Turn 2
        var history = await _service.GetRecentMessagesAsync(sessionId, limit: 10);

        // Assert
        Assert.Equal(3, history.Count);
        
        // Verify the history is in correct order
        Assert.Equal("user", history[0].Role);
        Assert.Equal("Show sales for today", history[0].Content);
        
        Assert.Equal("assistant", history[1].Role);
        Assert.Equal("Sales for today: ₱10,000", history[1].Content);
        
        Assert.Equal("user", history[2].Role);
        Assert.Equal("How about inventory?", history[2].Content);
    }

    [Fact]
    public async Task Multiple_Sessions_Should_Not_Interfere()
    {
        // Arrange - Create messages for two different sessions
        var session1 = Guid.NewGuid();
        var session2 = Guid.NewGuid();

        await _service.AddMessageToHistoryAsync(session1, "user", "Session 1 message");
        await _service.AddMessageToHistoryAsync(session2, "user", "Session 2 message");

        // Act
        var messages1 = await _service.GetRecentMessagesAsync(session1);
        var messages2 = await _service.GetRecentMessagesAsync(session2);

        // Assert
        Assert.Single(messages1);
        Assert.Equal("Session 1 message", messages1[0].Content);
        
        Assert.Single(messages2);
        Assert.Equal("Session 2 message", messages2[0].Content);
    }

    [Fact]
    public async Task GetRecentMessagesAsync_Should_Handle_Large_Limit()
    {
        // Arrange
        var sessionId = _testSessionId;
        await _service.AddMessageToHistoryAsync(sessionId, "user", "Message 1");
        await _service.AddMessageToHistoryAsync(sessionId, "assistant", "Response 1");

        // Act - Request more messages than exist
        var messages = await _service.GetRecentMessagesAsync(sessionId, limit: 100);

        // Assert - Should return all available messages (2)
        Assert.Equal(2, messages.Count);
    }

    [Fact]
    public async Task Message_Timestamps_Should_Be_Accurate()
    {
        // Arrange
        var sessionId = _testSessionId;
        var beforeSave = DateTime.UtcNow;

        // Act
        await _service.AddMessageToHistoryAsync(sessionId, "user", "Test message");
        var afterSave = DateTime.UtcNow;

        // Assert
        var messages = await _context.ChatMessages
            .Where(m => m.SessionId == sessionId)
            .ToListAsync();

        Assert.Single(messages);
        Assert.InRange(messages[0].CreatedAt, beforeSave.AddSeconds(-1), afterSave.AddSeconds(1));
    }
}
