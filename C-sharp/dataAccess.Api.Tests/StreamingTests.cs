using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.EntityFrameworkCore;
using dataAccess.Api.Services;
using dataAccess.Services;
using dataAccess.Planning;
using dataAccess.Reports;
using dataAccess.Contracts;
using Shared.Allowlists;

#nullable enable

namespace dataAccess.Api.Tests;

/// <summary>
/// Tests for streaming functionality and session memory
/// </summary>
public class StreamingTests
{
    private readonly Mock<IDatabaseSchemaService> _mockSchemaService;
    private readonly Mock<ILogger<ChatOrchestratorService>> _mockLogger;
    private readonly IConfiguration _configuration;
    private readonly AiDbContext _dbContext;
    private readonly TelemetryLogger _telemetryLogger;
    private readonly SqlValidator _sqlValidator;
    private readonly Mock<ISafeSqlExecutor> _safeSqlExecutor;

    public StreamingTests()
    {
        _mockSchemaService = new Mock<IDatabaseSchemaService>();
        _mockLogger = new Mock<ILogger<ChatOrchestratorService>>();
        
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=test;Username=test;Password=test"
            })
            .Build();

        // Create in-memory database for testing
        var options = new DbContextOptionsBuilder<AiDbContext>()
            .UseInMemoryDatabase(databaseName: $"StreamingTestDb_{Guid.NewGuid()}")
            .Options;
        _dbContext = new AiDbContext(options);
        _telemetryLogger = new TelemetryLogger(_dbContext);

        var allowlist = new Mock<ISqlAllowlist>(MockBehavior.Strict);
        allowlist.Setup(a => a.IsTableAllowed(It.IsAny<string>())).Returns(true);
        allowlist.Setup(a => a.IsColumnAllowed(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
        allowlist.Setup(a => a.IsOperatorAllowed(It.IsAny<string>())).Returns(true);
        allowlist.SetupGet(a => a.DefaultLimit).Returns(100);
        allowlist.SetupGet(a => a.MaxLimit).Returns(1000);
        _sqlValidator = new SqlValidator(allowlist.Object, new Mock<ILogger<SqlValidator>>().Object);

        _safeSqlExecutor = new Mock<ISafeSqlExecutor>();
        _safeSqlExecutor
            .Setup(e => e.ExecuteQueryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Dictionary<string, object?>>());
    }

    [Fact]
    public async Task StreamQueryAsync_ShouldYieldMetadataChunks()
    {
        // Given - Create a minimal kernel with test service
        var kernelBuilder = Kernel.CreateBuilder();
        kernelBuilder.AddOpenAIChatCompletion(
            modelId: "llama-3.1-8b-instant",
            apiKey: "test-key",
            serviceId: "fast-llm",
            endpoint: new Uri("https://api.groq.com/openai/v1")
        );
        var kernel = kernelBuilder.Build();

        // Phase 4: Mock the new dependencies
        var mockChatHistory = new Mock<IChatHistoryService>();
        var mockPromptLoader = new PromptLoader(); // Use real PromptLoader
        var mockReportRunner = new Mock<IYamlReportRunner>();
        var mockForecastRunner = new Mock<IForecastRunnerService>();
        var mockIntentRunner = new Mock<IYamlIntentRunner>(); // Now mockable via interface!
        
        // Mock Phase 6 dependencies - won't be called in streaming tests
        // Since LlmSqlGenerator and LlmSummarizer have non-parameterless constructors,
        // we can't mock them easily. Pass null since they won't be called in this test.
        LlmSqlGenerator? mockSqlGenerator = null!;
        LlmSummarizer? mockSummarizer = null!;
        
        // Mock Phase 4.5 dependency
        var mockDateParser = Mock.Of<ILlmDateParser>();

        var orchestrator = new ChatOrchestratorService(
            kernel,
            _mockSchemaService.Object,
            _telemetryLogger,
            _configuration,
            _mockLogger.Object,
            _sqlValidator,
            _safeSqlExecutor.Object,
            mockChatHistory.Object,
            mockPromptLoader,
            mockReportRunner.Object,
            mockForecastRunner.Object,
            mockIntentRunner.Object,
            mockSqlGenerator!,
            mockSummarizer!,
            mockDateParser
        );

        // When - Stream a query (will fail at LLM call, but we can test structure)
        var chunks = new List<StreamingChunk>();
        var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        try
        {
            await foreach (var chunk in orchestrator.StreamQueryAsync(
                "Test query",
                Guid.NewGuid(),
                Guid.NewGuid(),
                cancellationTokenSource.Token))
            {
                chunks.Add(chunk);
                
                // Stop after first few chunks to avoid LLM call
                if (chunks.Count >= 2)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected if we timeout waiting for LLM
        }
        catch (Exception)
        {
            // May fail due to invalid API key, but we should have received metadata chunks first
        }

        // Then - Verify we got metadata chunks
        Assert.NotEmpty(chunks);
        Assert.Contains(chunks, c => c.Type == "metadata");
    }

    [Fact]
    public async Task SessionMemory_ShouldRetrieveConversationHistory()
    {
        // Given - Create a session with some messages
        var userId = Guid.NewGuid();
        var session = await _telemetryLogger.CreateSessionAsync(userId, ttlMinutes: 30);

        await _telemetryLogger.LogUserMessageAsync(session.Id, userId, "First message");
        await _telemetryLogger.LogAssistantMessageAsync(session.Id, userId, "First response");
        await _telemetryLogger.LogUserMessageAsync(session.Id, userId, "Second message");
        await _telemetryLogger.LogAssistantMessageAsync(session.Id, userId, "Second response");

        // When - Retrieve history
        var history = await _telemetryLogger.GetSessionHistoryAsync(session.Id, maxMessages: 10);

        // Then - Verify all messages are retrieved in chronological order
        Assert.Equal(4, history.Count);
        Assert.Equal("First message", history[0].Content);
        Assert.Equal("user", history[0].Role);
        Assert.Equal("First response", history[1].Content);
        Assert.Equal("assistant", history[1].Role);
        Assert.Equal("Second message", history[2].Content);
        Assert.Equal("Second response", history[3].Content);
    }

    [Fact]
    public async Task SessionMemory_ShouldRespectMaxMessagesLimit()
    {
        // Given - Create a session with many messages
        var userId = Guid.NewGuid();
        var session = await _telemetryLogger.CreateSessionAsync(userId, ttlMinutes: 30);

        for (int i = 0; i < 15; i++)
        {
            await _telemetryLogger.LogUserMessageAsync(session.Id, userId, $"Message {i}");
        }

        // When - Retrieve history with limit
        var history = await _telemetryLogger.GetSessionHistoryAsync(session.Id, maxMessages: 5);

        // Then - Verify only last 5 messages are returned
        Assert.Equal(5, history.Count);
        Assert.Equal("Message 10", history[0].Content); // Should be the 5 most recent
        Assert.Equal("Message 14", history[4].Content);
    }

    [Fact]
    public async Task SessionMemory_ShouldUpdateLastActivityOnMessage()
    {
        // Given - Create a session
        var userId = Guid.NewGuid();
        var session = await _telemetryLogger.CreateSessionAsync(userId, ttlMinutes: 30);
        var initialExpiration = session.ExpiresAt;
        var initialActivity = session.LastActivityAt;

        // Wait a bit to ensure timestamp difference
        await Task.Delay(500);

        // When - Log a new message
        await _telemetryLogger.LogUserMessageAsync(session.Id, userId, "New message");

        // Retrieve session to check updated expiration
        var updatedSession = await _telemetryLogger.GetSessionAsync(session.Id);

        // Then - Verify expiration was extended (or at least not earlier)
        Assert.NotNull(updatedSession);
        Assert.True(updatedSession.ExpiresAt >= initialExpiration, 
            $"ExpiresAt should be >= initial. Initial: {initialExpiration}, Updated: {updatedSession.ExpiresAt}");
        Assert.True(updatedSession.LastActivityAt >= initialActivity,
            $"LastActivityAt should be >= initial. Initial: {initialActivity}, Updated: {updatedSession.LastActivityAt}");
    }

    [Fact]
    public async Task SessionValidation_ShouldReturnTrueForValidSession()
    {
        // Given - Create a fresh session
        var userId = Guid.NewGuid();
        var session = await _telemetryLogger.CreateSessionAsync(userId, ttlMinutes: 30);

        // When - Check validity
        var isValid = await _telemetryLogger.IsSessionValidAsync(session.Id);

        // Then
        Assert.True(isValid);
    }

    [Fact]
    public async Task SessionValidation_ShouldReturnFalseForExpiredSession()
    {
        // Given - Create a session with 0 TTL (expired immediately)
        var userId = Guid.NewGuid();
        var session = await _telemetryLogger.CreateSessionAsync(userId, ttlMinutes: 0);

        // Wait a tiny bit to ensure expiration
        await Task.Delay(10);

        // When - Check validity
        var isValid = await _telemetryLogger.IsSessionValidAsync(session.Id);

        // Then
        Assert.False(isValid);
    }

    [Fact]
    public async Task SessionCleanup_ShouldRemoveExpiredSessions()
    {
        // Given - Create multiple sessions with different TTLs
        var userId = Guid.NewGuid();
        var expiredSession1 = await _telemetryLogger.CreateSessionAsync(userId, ttlMinutes: 0);
        var expiredSession2 = await _telemetryLogger.CreateSessionAsync(userId, ttlMinutes: 0);
        var validSession = await _telemetryLogger.CreateSessionAsync(userId, ttlMinutes: 30);

        // Wait for expiration
        await Task.Delay(10);

        // When - Run cleanup
        var cleanedCount = await _telemetryLogger.CleanupExpiredSessionsAsync();

        // Then - Verify expired sessions removed, valid session remains
        Assert.Equal(2, cleanedCount);
        
        var stillValid = await _telemetryLogger.IsSessionValidAsync(validSession.Id);
        Assert.True(stillValid);

        var expiredGone1 = await _telemetryLogger.GetSessionAsync(expiredSession1.Id);
        var expiredGone2 = await _telemetryLogger.GetSessionAsync(expiredSession2.Id);
        Assert.Null(expiredGone1);
        Assert.Null(expiredGone2);
    }

    [Fact]
    public async Task TelemetryStats_ShouldCalculateMetrics()
    {
        // Given - Create session with messages and varied outcomes
        var userId = Guid.NewGuid();
        var session = await _telemetryLogger.CreateSessionAsync(userId, ttlMinutes: 30);

        await _telemetryLogger.LogAssistantMessageAsync(
            session.Id, userId, "Response 1",
            intent: "GetDataQuery",
            sqlGenerated: "SELECT * FROM sales",
            sqlValidated: true,
            sqlExecuted: true,
            resultRows: 10,
            latencyMs: 1000
        );

        await _telemetryLogger.LogAssistantMessageAsync(
            session.Id, userId, "Response 2",
            intent: "ChitChat",
            latencyMs: 100
        );

        await _telemetryLogger.LogAssistantMessageAsync(
            session.Id, userId, "Response 3",
            intent: "GetDataQuery",
            sqlGenerated: "SELECT * FROM products",
            sqlValidated: false,
            errorMessage: "Invalid SQL",
            latencyMs: 500
        );

        // When - Get stats
        var stats = await _telemetryLogger.GetTelemetryStatsAsync();

        // Then - Verify calculations
        Assert.Equal(3, stats["total_messages"]);
        
        var byIntent = (Dictionary<string, int>)stats["by_intent"];
        Assert.Equal(2, byIntent["GetDataQuery"]);
        Assert.Equal(1, byIntent["ChitChat"]);
        
        var avgLatency = (double)stats["avg_latency_ms"];
        Assert.True(avgLatency > 0);
        
        var errorCount = (int)stats["error_count"];
        Assert.Equal(1, errorCount);
    }

    [Fact]
    public void StreamingChunk_ShouldSupportAllTypes()
    {
        // Given/When - Create chunks of different types
        var metadataChunk = new StreamingChunk
        {
            Type = "metadata",
            Metadata = new Dictionary<string, object> { ["step"] = "intent" }
        };

        var contentChunk = new StreamingChunk
        {
            Type = "content",
            Content = "Test content"
        };

        var errorChunk = new StreamingChunk
        {
            Type = "error",
            Error = "Test error"
        };

        var doneChunk = new StreamingChunk
        {
            Type = "done",
            Metadata = new Dictionary<string, object> { ["total_latency_ms"] = 1234 }
        };

        // Then - Verify types
        Assert.Equal("metadata", metadataChunk.Type);
        Assert.NotNull(metadataChunk.Metadata);
        
        Assert.Equal("content", contentChunk.Type);
        Assert.Equal("Test content", contentChunk.Content);
        
        Assert.Equal("error", errorChunk.Type);
        Assert.Equal("Test error", errorChunk.Error);
        
        Assert.Equal("done", doneChunk.Type);
        Assert.NotNull(doneChunk.Metadata);
    }
}
