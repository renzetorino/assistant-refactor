using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using dataAccess.Api.Services;
using dataAccess.Services;
using dataAccess.Planning;
using dataAccess.Reports;
using dataAccess.Contracts;
using dataAccess.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Microsoft.SemanticKernel;
using Shared.Allowlists;

namespace dataAccess.Api.Tests;

/// <summary>
/// Integration tests for ChatOrchestratorService slot-filling refactor (Phase 4).
/// Tests validate the stateful, multi-turn conversation flow for clarification.
/// </summary>
public class ChatOrchestratorServiceTests
{
    private readonly Mock<IChatHistoryService> _mockChatHistory;
    private readonly Mock<IYamlReportRunner> _mockReportRunner;
    private readonly Mock<IForecastRunnerService> _mockForecastRunner;
    private readonly Mock<IYamlIntentRunner> _mockIntentRunner;
    private readonly PromptLoader _promptLoader; // Real PromptLoader for reading YAML files
    private readonly ChatOrchestratorService _orchestrator;
    private readonly Guid _testSessionId = Guid.NewGuid();
    private readonly Guid _testUserId = Guid.NewGuid();

    public ChatOrchestratorServiceTests()
    {
        // Setup mocks
        _mockChatHistory = new Mock<IChatHistoryService>();
        _mockReportRunner = new Mock<IYamlReportRunner>();
        _mockForecastRunner = new Mock<IForecastRunnerService>();
        _mockIntentRunner = new Mock<IYamlIntentRunner>(); // Now mockable via interface!
        
        // Setup default mocks for conversational memory methods
        _mockChatHistory
            .Setup(x => x.GetRecentMessagesAsync(It.IsAny<Guid>(), It.IsAny<int>()))
            .ReturnsAsync(new List<ChatMessage>()); // Return empty history by default
        
        _mockChatHistory
            .Setup(x => x.AddMessageToHistoryAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        
        // Use real PromptLoader - test project copies YAML files to output directory
        _promptLoader = new PromptLoader();

        // Setup base infrastructure dependencies
        var kernelBuilder = Kernel.CreateBuilder();
        kernelBuilder.AddOpenAIChatCompletion(
            modelId: "test-model",
            apiKey: "test-key",
            serviceId: "test-service",
            endpoint: new Uri("https://test.com")
        );
        var kernel = kernelBuilder.Build();

        var schemaService = new Mock<IDatabaseSchemaService>().Object;
        var options = new DbContextOptionsBuilder<AiDbContext>()
            .UseInMemoryDatabase($"TelemetryDb_{Guid.NewGuid()}")
            .Options;
        var telemetryLogger = new TelemetryLogger(new AiDbContext(options));
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var logger = new Mock<ILogger<ChatOrchestratorService>>().Object;
        var sqlValidator = CreateValidator();
        var safeSqlExecutor = new Mock<ISafeSqlExecutor>().Object;
        
        // Mock Phase 6 dependencies - these won't be called in our report tests
        // Since LlmSqlGenerator and LlmSummarizer have non-parameterless constructors,
        // we can't mock them easily. Pass null since they won't be called in report flow.
        LlmSqlGenerator? mockSqlGenerator = null!;
        LlmSummarizer? mockSummarizer = null!;
        
        // Mock Phase 4.5 dependency
        var mockDateParser = new Mock<ILlmDateParser>();
        mockDateParser
            .Setup(x => x.ParseDateRangeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTime.Today.AddDays(-1), DateTime.Today.AddDays(-1))); // Default to yesterday

        // Create orchestrator with mocked Phase 4 dependencies
        _orchestrator = new ChatOrchestratorService(
            kernel,
            schemaService,
            telemetryLogger,
            config,
            logger,
            sqlValidator,
            safeSqlExecutor,
            _mockChatHistory.Object,
            _promptLoader, // Use real PromptLoader
            _mockReportRunner.Object,
            _mockForecastRunner.Object,
            _mockIntentRunner.Object,
            mockSqlGenerator!,
            mockSummarizer!,
            mockDateParser.Object
        );
    }

    private static SqlValidator CreateValidator()
    {
        var allowlist = new Mock<ISqlAllowlist>(MockBehavior.Strict);
        allowlist.Setup(a => a.IsTableAllowed(It.IsAny<string>())).Returns(true);
        allowlist.Setup(a => a.IsColumnAllowed(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
        allowlist.Setup(a => a.IsOperatorAllowed(It.IsAny<string>())).Returns(true);
        allowlist.SetupGet(a => a.DefaultLimit).Returns(100);
        allowlist.SetupGet(a => a.MaxLimit).Returns(1000);

        var validatorLogger = new Mock<ILogger<SqlValidator>>().Object;
        return new SqlValidator(allowlist.Object, validatorLogger);
    }

    /// <summary>
    /// Test 1: Two-stage clarification flow
    /// Turn 1: "Create a report" → Stage 1 clarification (ambiguous topic)
    /// Turn 2: "Sales" → Stage 2 clarification (missing date range)
    /// </summary>
    [Fact]
    public async Task Should_Handle_Stage_1_Then_Stage_2_Clarification()
    {
        // ═══════════════════════════════════════════════════════════════
        // TURN 1: User says "Create a report"
        // ═══════════════════════════════════════════════════════════════
        
        // Setup: No pending state initially
        var mockSession = new ChatSession
        {
            Id = _testSessionId,
            UserId = _testUserId,
            StartedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow,
            PendingPlanJson = null,
            PendingSlotName = null
        };

        _mockChatHistory
            .Setup(x => x.GetOrCreateSessionAsync(_testSessionId.ToString()))
            .ReturnsAsync(mockSession);

        _mockChatHistory
            .Setup(x => x.GetPendingState(It.IsAny<ChatSession>()))
            .Returns((null as JsonDocument, null as string));

        // Mock intent classification: "report" with no sub_intent
        var intentResult = JsonDocument.Parse(@"{
            ""intent"": ""report"",
            ""domain"": ""business"",
            ""sub_intent"": """",
            ""confidence"": 0.95
        }");

        _mockIntentRunner
            .Setup(x => x.RunIntentAsync("Create a report", It.IsAny<List<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(intentResult);

        // Note: GetClarificationPromptForIntent reads from router.intent.yaml file (copied to test output)

        // Execute Turn 1
        var result1 = await _orchestrator.HandleQueryAsync(
            "Create a report",
            _testUserId,
            _testSessionId,
            CancellationToken.None);

        // Assert Turn 1: Stage 1 clarification
        Assert.True(result1.IsSuccess);
        Assert.Contains("what", result1.Response, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("report", result1.Response, StringComparison.OrdinalIgnoreCase);

        // Verify pending state saved with "sub_intent" slot
        _mockChatHistory.Verify(
            x => x.SavePendingStateAsync(
                _testSessionId,
                It.Is<JsonDocument>(doc => doc.RootElement.GetProperty("Intent").GetString() == "report"),
                "sub_intent"),
            Times.Once);

        // ═══════════════════════════════════════════════════════════════
        // TURN 2: User says "Sales"
        // ═══════════════════════════════════════════════════════════════

        // Setup: Restore pending state from Turn 1
        var pendingPlan = new PlannerResult
        {
            Intent = "report",
            Domain = "business",
            SubIntent = "",
            Confidence = 0.95,
            UserText = "Create a report"
        };

        _mockChatHistory
            .Setup(x => x.GetPendingState(It.IsAny<ChatSession>()))
            .Returns((pendingPlan.ToJsonDocument(), "sub_intent"));

        _mockChatHistory
            .Setup(x => x.ClearPendingStateAsync(_testSessionId))
            .Returns(Task.CompletedTask);

        // Mock report runner: Missing "period_start" parameter
        var clarificationResult = new OrchestrationStepResult
        {
            RequiresClarification = true,
            MissingParameterName = "period_start",
            ClarificationPrompt = "Sure, for what date range would you like that report?",
            PendingPlan = new PlannerResult
            {
                Intent = "report",
                SubIntent = "Sales",
                Domain = "business",
                Confidence = 0.95
            }.ToJsonDocument()
        };

        _mockReportRunner
            .Setup(x => x.RunReportAsync(
                It.Is<string>(s => s.Contains("sales") || s == "report"),
                It.Is<PlannerResult>(p => p.Intent == "report" && p.SubIntent == "Sales"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(clarificationResult);

        // Execute Turn 2
        var result2 = await _orchestrator.HandleQueryAsync(
            "Sales",
            _testUserId,
            _testSessionId,
            CancellationToken.None);

        // Assert Turn 2: Stage 2 clarification
        Assert.True(result2.IsSuccess);
        Assert.Contains("date range", result2.Response, StringComparison.OrdinalIgnoreCase);

        // Verify cleared previous pending state
        _mockChatHistory.Verify(
            x => x.ClearPendingStateAsync(_testSessionId),
            Times.Once);

        // Verify new pending state saved with "period_start" slot
        _mockChatHistory.Verify(
            x => x.SavePendingStateAsync(
                _testSessionId,
                It.Is<JsonDocument>(doc => doc.RootElement.GetProperty("SubIntent").GetString() == "Sales"),
                "period_start"),
            Times.Once);
    }

    /// <summary>
    /// Test 2: Skip Stage 1, go directly to Stage 2 clarification
    /// Turn 1: "Create a sales report" → Stage 2 clarification (missing date range)
    /// </summary>
    [Fact]
    public async Task Should_Skip_Stage_1_And_Handle_Stage_2_Clarification()
    {
        // Setup: No pending state
        var mockSession = new ChatSession
        {
            Id = _testSessionId,
            UserId = _testUserId,
            PendingPlanJson = null,
            PendingSlotName = null
        };

        _mockChatHistory
            .Setup(x => x.GetOrCreateSessionAsync(_testSessionId.ToString()))
            .ReturnsAsync(mockSession);

        _mockChatHistory
            .Setup(x => x.GetPendingState(It.IsAny<ChatSession>()))
            .Returns((null as JsonDocument, null as string));

        // Mock intent classification: "report" WITH sub_intent "Sales"
        var intentResult = JsonDocument.Parse(@"{
            ""intent"": ""report"",
            ""domain"": ""sales"",
            ""sub_intent"": ""Sales"",
            ""confidence"": 0.98
        }");

        _mockIntentRunner
            .Setup(x => x.RunIntentAsync("Create a sales report", It.IsAny<List<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(intentResult);

        // Mock report runner: Missing "period_start" parameter
        var clarificationResult = new OrchestrationStepResult
        {
            RequiresClarification = true,
            MissingParameterName = "period_start",
            ClarificationPrompt = "Sure, for what date range would you like that report?",
            PendingPlan = new PlannerResult
            {
                Intent = "report",
                SubIntent = "Sales",
                Domain = "sales",
                Confidence = 0.98
            }.ToJsonDocument()
        };

        _mockReportRunner
            .Setup(x => x.RunReportAsync(
                It.Is<string>(s => s.Contains("sales") || s == "report"),
                It.Is<PlannerResult>(p => p.Intent == "report" && p.SubIntent == "Sales"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(clarificationResult);

        // Execute
        var result = await _orchestrator.HandleQueryAsync(
            "Create a sales report",
            _testUserId,
            _testSessionId,
            CancellationToken.None);

        // Assert: Stage 2 clarification (skipped Stage 1)
        Assert.True(result.IsSuccess);
        Assert.Contains("date range", result.Response, StringComparison.OrdinalIgnoreCase);

        // Verify: SavePendingStateAsync was called for "period_start", NOT "sub_intent"
        _mockChatHistory.Verify(
            x => x.SavePendingStateAsync(
                _testSessionId,
                It.Is<JsonDocument>(doc => doc.RootElement.GetProperty("SubIntent").GetString() == "Sales"),
                "period_start"),
            Times.Once);

        // Verify: SavePendingStateAsync was NEVER called for "sub_intent"
        _mockChatHistory.Verify(
            x => x.SavePendingStateAsync(
                It.IsAny<Guid>(),
                It.IsAny<JsonDocument>(),
                "sub_intent"),
            Times.Never);
    }

    /// <summary>
    /// Test 3: Complete flow with no clarification needed
    /// Turn 1: "Create a sales report for yesterday" → Execute immediately
    /// </summary>
    [Fact]
    public async Task Should_Execute_Directly_With_No_Clarification()
    {
        // Setup: No pending state
        var mockSession = new ChatSession
        {
            Id = _testSessionId,
            UserId = _testUserId,
            PendingPlanJson = null,
            PendingSlotName = null
        };

        _mockChatHistory
            .Setup(x => x.GetOrCreateSessionAsync(_testSessionId.ToString()))
            .ReturnsAsync(mockSession);

        _mockChatHistory
            .Setup(x => x.GetPendingState(It.IsAny<ChatSession>()))
            .Returns((null as JsonDocument, null as string));

        // Mock intent classification: "report" with sub_intent and slots filled
        var intentResult = JsonDocument.Parse(@"{
            ""intent"": ""report"",
            ""domain"": ""sales"",
            ""sub_intent"": ""Sales"",
            ""confidence"": 0.98,
            ""period_start"": ""yesterday"",
            ""period_end"": ""yesterday""
        }");

        _mockIntentRunner
            .Setup(x => x.RunIntentAsync("Create a sales report for yesterday", It.IsAny<List<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(intentResult);

        // Mock report runner: Successful execution (no clarification)
        var successResult = new OrchestrationStepResult
        {
            IsSuccess = true,
            RequiresClarification = false,
            ReportData = new ReportResult
            {
                Id = Guid.NewGuid(),
                Title = "Sales Report",
                PeriodLabel = "Yesterday",
                UiSpec = JsonDocument.Parse(@"{""kpis"": [], ""charts"": []}")
            }
        };

        _mockReportRunner
            .Setup(x => x.RunReportAsync(
                It.Is<string>(s => s.Contains("sales") || s == "report"),
                It.Is<PlannerResult>(p => 
                    p.Intent == "report" && 
                    p.SubIntent == "Sales"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(successResult);

        // Execute
        var result = await _orchestrator.HandleQueryAsync(
            "Create a sales report for yesterday",
            _testUserId,
            _testSessionId,
            CancellationToken.None);

        // Assert: Immediate execution (no clarification)
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Response);
        Assert.Contains("Sales Report", result.Response);

        // Verify: RunReportAsync was called
        _mockReportRunner.Verify(
            x => x.RunReportAsync(
                It.Is<string>(s => s.Contains("sales") || s == "report"),
                It.Is<PlannerResult>(p => p.SubIntent == "Sales"),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify: SavePendingStateAsync was NEVER called
        _mockChatHistory.Verify(
            x => x.SavePendingStateAsync(
                It.IsAny<Guid>(),
                It.IsAny<JsonDocument>(),
                It.IsAny<string>()),
            Times.Never);
    }

    /// <summary>
    /// Test 4: Complete 3-turn conversation flow (from master documentation example)
    /// Turn 1: "Create a report" → Stage 1 clarification
    /// Turn 2: "Sales" → Stage 2 clarification
    /// Turn 3: "Yesterday" → Execute report
    /// </summary>
    [Fact]
    public async Task Should_Complete_Full_3_Turn_Conversation_Flow()
    {
        // ═══════════════════════════════════════════════════════════════
        // TURN 1: "Create a report"
        // ═══════════════════════════════════════════════════════════════
        var mockSession = new ChatSession
        {
            Id = _testSessionId,
            UserId = _testUserId,
            PendingPlanJson = null,
            PendingSlotName = null
        };

        _mockChatHistory
            .Setup(x => x.GetOrCreateSessionAsync(_testSessionId.ToString()))
            .ReturnsAsync(mockSession);

        _mockChatHistory
            .Setup(x => x.GetPendingState(It.IsAny<ChatSession>()))
            .Returns((null as JsonDocument, null as string));

        var intentResult1 = JsonDocument.Parse(@"{
            ""intent"": ""report"",
            ""domain"": ""business"",
            ""sub_intent"": """",
            ""confidence"": 0.95
        }");

        _mockIntentRunner
            .Setup(x => x.RunIntentAsync("Create a report", It.IsAny<List<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(intentResult1);

        // Note: In real implementation, GetClarificationPromptForIntent reads from router.intent.yaml file
        // For testing, we verify the clarification response pattern

        var result1 = await _orchestrator.HandleQueryAsync(
            "Create a report", _testUserId, _testSessionId);

        Assert.Contains("what", result1.Response, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("report", result1.Response, StringComparison.OrdinalIgnoreCase);

        // ═══════════════════════════════════════════════════════════════
        // TURN 2: "Sales"
        // ═══════════════════════════════════════════════════════════════
        var pendingPlan1 = new PlannerResult
        {
            Intent = "report",
            Domain = "business",
            SubIntent = "",
            Confidence = 0.95
        };

        _mockChatHistory
            .Setup(x => x.GetPendingState(It.IsAny<ChatSession>()))
            .Returns((pendingPlan1.ToJsonDocument(), "sub_intent"));

        _mockChatHistory
            .Setup(x => x.ClearPendingStateAsync(_testSessionId))
            .Returns(Task.CompletedTask);

        var clarificationResult2 = new OrchestrationStepResult
        {
            RequiresClarification = true,
            MissingParameterName = "period_start",
            ClarificationPrompt = "Sure, for what date range would you like that report?",
            PendingPlan = new PlannerResult
            {
                Intent = "report",
                SubIntent = "Sales",
                Domain = "business"
            }.ToJsonDocument()
        };

        _mockReportRunner
            .Setup(x => x.RunReportAsync(
                It.Is<string>(s => s.Contains("sales") || s == "report"),
                It.Is<PlannerResult>(p => p.SubIntent == "Sales"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(clarificationResult2);

        var result2 = await _orchestrator.HandleQueryAsync(
            "Sales", _testUserId, _testSessionId);

        Assert.Contains("date range", result2.Response, StringComparison.OrdinalIgnoreCase);

        // ═══════════════════════════════════════════════════════════════
        // TURN 3: "Yesterday"
        // ═══════════════════════════════════════════════════════════════
        var pendingPlan2 = new PlannerResult
        {
            Intent = "report",
            SubIntent = "Sales",
            Domain = "business",
            Confidence = 0.95,
            Slots = new Dictionary<string, string>()
        };

        _mockChatHistory
            .Setup(x => x.GetPendingState(It.IsAny<ChatSession>()))
            .Returns((pendingPlan2.ToJsonDocument(), "period_start"));

        var successResult = new OrchestrationStepResult
        {
            IsSuccess = true,
            RequiresClarification = false,
            ReportData = new ReportResult
            {
                Id = Guid.NewGuid(),
                Title = "Sales Report",
                PeriodLabel = "Yesterday (2025-11-09)",
                UiSpec = JsonDocument.Parse(@"{
                    ""report_title"": ""Sales Report"",
                    ""period"": {""start"": ""2025-11-09"", ""end"": ""2025-11-09""},
                    ""kpis"": [],
                    ""charts"": []
                }")
            }
        };

        _mockReportRunner
            .Setup(x => x.RunReportAsync(
                It.Is<string>(s => s.Contains("sales") || s == "report"),
                It.Is<PlannerResult>(p => 
                    p.SubIntent == "Sales" && 
                    p.Slots.ContainsKey("period_start") && 
                    p.Slots["period_start"] == "Yesterday"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(successResult);

        var result3 = await _orchestrator.HandleQueryAsync(
            "Yesterday", _testUserId, _testSessionId);

        Assert.True(result3.IsSuccess);
        Assert.Contains("Sales Report", result3.Response);

        // Verify full conversation flow completed
        _mockChatHistory.Verify(
            x => x.SavePendingStateAsync(It.IsAny<Guid>(), It.IsAny<JsonDocument>(), "sub_intent"),
            Times.AtLeastOnce);
        _mockChatHistory.Verify(
            x => x.SavePendingStateAsync(It.IsAny<Guid>(), It.IsAny<JsonDocument>(), "period_start"),
            Times.AtLeastOnce);
        _mockChatHistory.Verify(
            x => x.ClearPendingStateAsync(_testSessionId),
            Times.AtLeast(2)); // Cleared twice (after Turn 1 and Turn 2)
        _mockReportRunner.Verify(
            x => x.RunReportAsync(It.IsAny<string>(), It.IsAny<PlannerResult>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2)); // Called in Turn 2 and Turn 3
    }

    [Fact]
    public async Task Should_Handle_Forecast_Clarification_And_Execution()
    {
        // ═══════════════════════════════════════════════════════════════
        // SCENARIO: User requests a forecast but doesn't specify days
        // Expected: Stage 2 clarification for "forecast_days" slot
        // ═══════════════════════════════════════════════════════════════

        // ═══════════════════════════════════════════════════════════════
        // TURN 1: User says "forecast my sales"
        // ═══════════════════════════════════════════════════════════════

        // Mock intent classification (forecast.sales with sub_intent, but no forecast_days slot)
        var intentResult = JsonDocument.Parse(@"{
            ""intent"": ""forecast"",
            ""domain"": ""sales"",
            ""sub_intent"": ""sales"",
            ""confidence"": 0.95,
            ""slots"": {}
        }");

        _mockIntentRunner
            .Setup(x => x.RunIntentAsync("forecast my sales", It.IsAny<List<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(intentResult);

        // Mock session state (no pending state initially)
        var testSession = new ChatSession
        {
            Id = _testSessionId,
            UserId = _testUserId,
            StartedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow,
            PendingPlanJson = null,
            PendingSlotName = null
        };

        _mockChatHistory
            .Setup(x => x.GetOrCreateSessionAsync(_testSessionId.ToString()))
            .ReturnsAsync(testSession);

        _mockChatHistory
            .Setup(x => x.GetPendingState(It.IsAny<ChatSession>()))
            .Returns(((JsonDocument)null, (string)null));

        // Mock ForecastRunner to return clarification request for missing forecast_days
        var clarificationResult = new OrchestrationStepResult
        {
            IsSuccess = false,
            RequiresClarification = true,
            MissingParameterName = "forecast_days",
            ClarificationPrompt = "Sure, for how many days into the future would you like to forecast?",
            PendingPlan = new PlannerResult
            {
                Intent = "forecast",
                SubIntent = "sales",
                Domain = "sales",
                Confidence = 0.95,
                Slots = new Dictionary<string, string>()
            }.ToJsonDocument()
        };

        _mockForecastRunner
            .Setup(x => x.RunForecastAsync(
                It.Is<PlannerResult>(p => 
                    p.Intent == "forecast" && 
                    p.SubIntent == "sales" &&
                    !p.Slots.ContainsKey("forecast_days")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(clarificationResult);

        // Execute Turn 1
        var result1 = await _orchestrator.HandleQueryAsync(
            "forecast my sales",
            _testUserId,
            _testSessionId,
            CancellationToken.None);

        // Assert Turn 1: Stage 2 clarification for forecast_days
        Assert.True(result1.IsSuccess);
        Assert.Contains("how many days", result1.Response, StringComparison.OrdinalIgnoreCase);

        // Verify pending state saved with "forecast_days" slot
        _mockChatHistory.Verify(
            x => x.SavePendingStateAsync(
                _testSessionId,
                It.IsAny<JsonDocument>(),
                "forecast_days"),
            Times.Once);

        // ═══════════════════════════════════════════════════════════════
        // TURN 2: User says "30 days"
        // ═══════════════════════════════════════════════════════════════

        // Mock pending state from Turn 1
        var pendingPlan = new PlannerResult
        {
            Intent = "forecast",
            SubIntent = "sales",
            Domain = "sales",
            Confidence = 0.95,
            Slots = new Dictionary<string, string>()
        };

        _mockChatHistory
            .Setup(x => x.GetPendingState(It.IsAny<ChatSession>()))
            .Returns((pendingPlan.ToJsonDocument(), "forecast_days"));

        // Mock forecast runner success
        var forecastResult = new OrchestrationStepResult
        {
            IsSuccess = true,
            RequiresClarification = false,
            ReportData = new ReportResult
            {
                Id = Guid.NewGuid(),
                Title = "Sales Forecast",
                PeriodLabel = "Next 30 days",
                UiSpec = JsonDocument.Parse(@"{
                    ""report_title"": ""Sales Forecast"",
                    ""period"": {""label"": ""Next 30 days""},
                    ""forecast_days"": 30,
                    ""kpis"": [
                        {""label"": ""Forecasted Total"", ""value"": 150000},
                        {""label"": ""Last 7d Actual"", ""value"": 35000},
                        {""label"": ""Last 28d Actual"", ""value"": 140000}
                    ],
                    ""charts"": [{
                        ""type"": ""line"",
                        ""title"": ""Sales Forecast"",
                        ""series"": [
                            {""name"": ""Historical"", ""points"": []},
                            {""name"": ""Forecast"", ""points"": [], ""style"": ""dashed""}
                        ]
                    }]
                }")
            }
        };

        _mockForecastRunner
            .Setup(x => x.RunForecastAsync(
                It.Is<PlannerResult>(p => 
                    p.Intent == "forecast" &&
                    p.SubIntent == "sales" &&
                    p.Slots.ContainsKey("forecast_days") &&
                    p.Slots["forecast_days"] == "30 days"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(forecastResult);

        // Execute Turn 2
        var result2 = await _orchestrator.HandleQueryAsync(
            "30 days",
            _testUserId,
            _testSessionId,
            CancellationToken.None);

        // Assert Turn 2: Successful forecast execution
        Assert.True(result2.IsSuccess);
        Assert.Contains("Sales Forecast", result2.Response);

        // Verify forecast runner was called
        _mockForecastRunner.Verify(
            x => x.RunForecastAsync(
                It.Is<PlannerResult>(p => 
                    p.Intent == "forecast" &&
                    p.Slots.ContainsKey("forecast_days")),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify pending state was cleared after execution
        _mockChatHistory.Verify(
            x => x.ClearPendingStateAsync(_testSessionId),
            Times.Once);
    }
}
