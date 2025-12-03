using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using dataAccess.Api.Services;
using dataAccess.Api.Contracts;
using dataAccess.Services;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace dataAccess.Api.Controllers;

/// <summary>
/// Chat-based AI assistant for business intelligence queries using natural language.
/// Supports data queries, business policy questions, and general assistance.
/// </summary>
[ApiController]
[Authorize(Policy = "ApiUser")]
[Route("api/[controller]")]
[Produces("application/json")]
public class ChatController : ControllerBase
{
    private readonly IChatOrchestratorService _orchestrator;
    private readonly TelemetryLogger _telemetryLogger;
    private readonly ILogger<ChatController> _logger;

    public ChatController(
        IChatOrchestratorService orchestrator,
        TelemetryLogger telemetryLogger,
        ILogger<ChatController> logger)
    {
        _orchestrator = orchestrator;
        _telemetryLogger = telemetryLogger;
        _logger = logger;
    }

    /// <summary>
    /// Process a chat query with non-streaming response
    /// </summary>
    /// <param name="request">Chat query request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Chat response with AI-generated answer</returns>
    /// <remarks>
    /// Sample request for data query:
    /// 
    ///     POST /api/chat/query
    ///     {
    ///       "query": "Magkano ang total sales natin today?",
    ///       "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    ///       "sessionId": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
    ///     }
    /// 
    /// Sample request for business policy question:
    /// 
    ///     POST /api/chat/query
    ///     {
    ///       "query": "Paano mag-file ng vacation leave?",
    ///       "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
    ///     }
    /// 
    /// </remarks>
    /// <response code="200">Returns the AI-generated response</response>
    /// <response code="400">If the request is invalid</response>
    /// <response code="500">If an internal error occurs</response>
    [HttpPost("query")]
    [ProducesResponseType(typeof(ChatQueryResponse), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 500)]
    public async Task<ActionResult<ChatQueryResponse>> QueryAsync(
        [FromBody] ChatQueryRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            // SECURITY: Resolve caller identity from JWT only (not from request body)
            if (!TryResolveCallerUserId(out var callerUserId, out var authFailure))
            {
                return authFailure!;
            }

            _logger.LogInformation("Received chat query from user {UserId}: {Query}", callerUserId, request.Query);

            // Ensure a canonical session exists for this request
            Guid sessionId;
            if (request.SessionId.HasValue)
            {
                sessionId = request.SessionId.Value;

                // SECURITY: Validate session ownership before processing
                var session = await _telemetryLogger.GetSessionAsync(sessionId, cancellationToken);
                if (session == null || session.ExpiresAt <= DateTime.UtcNow)
                {
                    return NotFound(new ProblemDetails
                    {
                        Status = 404,
                        Title = "Session Not Found",
                        Detail = $"Session {sessionId} not found or has expired."
                    });
                }

                if (session.UserId != callerUserId)
                {
                    _logger.LogWarning(
                        "User {UserId} attempted to access session {SessionId} owned by {OwnerId}",
                        callerUserId,
                        sessionId,
                        session.UserId
                    );

                    return Forbid();
                }
            }
            else
            {
                var session = await _telemetryLogger.CreateSessionAsync(
                    callerUserId,
                    ct: cancellationToken
                );

                sessionId = session.Id;
                _logger.LogInformation(
                    "Created new chat session {SessionId} for user {UserId} during query handling.",
                    sessionId,
                    callerUserId
                );
            }

            // Log user message to telemetry using the canonical session
            await _telemetryLogger.LogUserMessageAsync(
                sessionId,
                callerUserId,
                request.Query,
                ct: cancellationToken
            );

            // Process query through orchestrator
            var result = await _orchestrator.HandleQueryAsync(
                request.Query,
                callerUserId,
                sessionId,
                cancellationToken
            );

            // Persist assistant telemetry to mirror streaming flow parity
            try
            {
                var pipelineMetadata = JsonSerializer.Serialize(new
                {
                    pipeline = "sk-orchestrator-v1",
                    step1_intent_classification_ms = result.IntentClassificationLatencyMs,
                    step2_sql_generation_ms = result.SqlGenerationLatencyMs,
                    step3_query_execution_ms = result.QueryExecutionLatencyMs,
                    step4_summarization_ms = result.SummarizationLatencyMs
                });

                await _telemetryLogger.LogAssistantMessageAsync(
                    sessionId,
                    callerUserId,
                    result.Response ?? string.Empty,
                    intent: result.Intent,
                    sqlGenerated: result.GeneratedSql,
                    sqlValidated: result.SqlValidated,
                    sqlExecuted: result.SqlExecuted,
                    resultRows: result.ResultCount,
                    resultSummary: pipelineMetadata,
                    latencyMs: result.TotalLatencyMs,
                    errorMessage: result.ErrorMessage,
                    ct: cancellationToken
                );
            }
            catch (Exception telemetryEx)
            {
                _logger.LogError(telemetryEx, "Failed to log assistant telemetry for session {SessionId}", sessionId);
            }

            // Map to response DTO
            var response = new ChatQueryResponse
            {
                UserQuery = result.UserQuery,
                Response = result.Response ?? string.Empty,
                Intent = result.Intent,
                SessionId = sessionId,
                UserId = result.UserId,
                IsSuccess = result.IsSuccess,
                ErrorMessage = result.ErrorMessage,
                GeneratedSql = result.GeneratedSql,
                ResultCount = result.ResultCount,
                TotalLatencyMs = result.TotalLatencyMs,
                Latency = new LatencyBreakdown
                {
                    IntentClassificationMs = result.IntentClassificationLatencyMs,
                    SqlGenerationMs = result.SqlGenerationLatencyMs,
                    QueryExecutionMs = result.QueryExecutionLatencyMs,
                    SummarizationMs = result.SummarizationLatencyMs
                }
            };

            _logger.LogInformation(
                "Query processed successfully in {LatencyMs}ms. Intent: {Intent}, Success: {IsSuccess}",
                response.TotalLatencyMs,
                response.Intent,
                response.IsSuccess
            );

            return Ok(response);
        }
        catch (Exception ex)
        {
            var errorId = Guid.NewGuid();
            var sessionIdForError = request.SessionId ?? Guid.NewGuid();
            
            // Try to get userId from JWT if available, otherwise use empty GUID
            Guid userIdForError = Guid.Empty;
            if (TryResolveCallerUserId(out var resolvedUserId, out _))
            {
                userIdForError = resolvedUserId;
            }
            
            // CRITICAL: Log FULL exception details for debugging
            _logger.LogError(ex, 
                "FATAL: Unhandled exception in ChatController processing query for SessionId {SessionId}. " +
                "ErrorId: {ErrorId}. Exception: {Exception}", 
                sessionIdForError, errorId, ex.ToString());

            // CRITICAL: Return ChatQueryResponse (NOT ProblemDetails) so React UI can unstuck from loading state
            var errorResponse = new ChatQueryResponse
            {
                UserQuery = request.Query,
                Response = $"An internal server error occurred. Please contact support with Error ID: {errorId}",
                Intent = "error",
                SessionId = sessionIdForError,
                UserId = userIdForError,
                IsSuccess = false,
                ErrorMessage = $"{ex.GetType().Name}: {ex.Message}",
                GeneratedSql = null,
                ResultCount = null,
                TotalLatencyMs = 0,
                Latency = new LatencyBreakdown
                {
                    IntentClassificationMs = 0,
                    SqlGenerationMs = 0,
                    QueryExecutionMs = 0,
                    SummarizationMs = 0
                }
            };

            return StatusCode(500, errorResponse);
        }
    }

    /// <summary>
    /// Process a chat query with Server-Sent Events (SSE) streaming response
    /// </summary>
    /// <param name="request">Chat query request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>SSE stream of response chunks</returns>
    [HttpPost("stream")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 500)]
    public async Task StreamAsync(
        [FromBody] ChatQueryRequest request,
        CancellationToken cancellationToken)
    {
        Guid sessionId = Guid.Empty;
        Guid? assistantMessageId = null;
        var assistantContentBuilder = new StringBuilder();
        string? detectedIntent = null;
        string? generatedSql = null;
        int? resultCount = null;
        int? totalLatencyMs = null;
        string? errorMessage = null;

        try
        {
            // SECURITY: Resolve caller identity from JWT only (not from request body)
            if (!TryResolveCallerUserId(out var callerUserId, out var authFailure))
            {
                // Cannot return ActionResult from async Task method, send error chunk instead
                var errorChunk = new StreamingChunkDto
                {
                    Type = "error",
                    Error = "Unauthorized: User identity could not be resolved from JWT token."
                };

                var json = JsonSerializer.Serialize(errorChunk, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                await Response.WriteAsync($"data: {json}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
                return;
            }

            _logger.LogInformation("Received streaming chat query from user {UserId}: {Query}", callerUserId, request.Query);

            // Set headers for SSE
            Response.Headers.Append("Content-Type", "text/event-stream");
            Response.Headers.Append("Cache-Control", "no-cache");
            Response.Headers.Append("Connection", "keep-alive");
            Response.Headers.Append("X-Accel-Buffering", "no"); // Disable proxy buffering (nginx)

            // Ensure a canonical session exists for streaming requests
            if (request.SessionId.HasValue)
            {
                sessionId = request.SessionId.Value;

                // SECURITY: Validate session ownership before processing
                var session = await _telemetryLogger.GetSessionAsync(sessionId, cancellationToken);
                if (session == null || session.ExpiresAt <= DateTime.UtcNow)
                {
                    var errorChunk = new StreamingChunkDto
                    {
                        Type = "error",
                        Error = $"Session {sessionId} not found or has expired."
                    };

                    var json = JsonSerializer.Serialize(errorChunk, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    });

                    await Response.WriteAsync($"data: {json}\n\n", cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken);
                    return;
                }

                if (session.UserId != callerUserId)
                {
                    _logger.LogWarning(
                        "User {UserId} attempted to access session {SessionId} owned by {OwnerId}",
                        callerUserId,
                        sessionId,
                        session.UserId
                    );

                    var errorChunk = new StreamingChunkDto
                    {
                        Type = "error",
                        Error = "Forbidden: You do not have access to this session."
                    };

                    var json = JsonSerializer.Serialize(errorChunk, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    });

                    await Response.WriteAsync($"data: {json}\n\n", cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken);
                    return;
                }
            }
            else
            {
                var session = await _telemetryLogger.CreateSessionAsync(
                    callerUserId,
                    ct: cancellationToken
                );

                sessionId = session.Id;
                _logger.LogInformation(
                    "Created new chat session {SessionId} for user {UserId} during streaming query handling.",
                    sessionId,
                    callerUserId
                );
            }

            // Log user message to telemetry and prepare assistant placeholder for streaming
            await _telemetryLogger.LogUserMessageAsync(
                sessionId,
                callerUserId,
                request.Query,
                ct: cancellationToken
            );

            assistantMessageId = (await _telemetryLogger.CreateAssistantMessagePlaceholderAsync(
                sessionId,
                callerUserId,
                ct: cancellationToken
            )).Id;

            // Inform the client of the canonical session identifier
            var sessionChunk = new StreamingChunkDto
            {
                Type = "metadata",
                Metadata = new Dictionary<string, object>
                {
                    ["step"] = "session",
                    ["sessionId"] = sessionId
                }
            };

            var sessionJson = JsonSerializer.Serialize(sessionChunk, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            await Response.WriteAsync($"data: {sessionJson}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);

            // Stream query through orchestrator
            await foreach (var chunk in _orchestrator.StreamQueryAsync(
                request.Query,
                callerUserId,
                sessionId,
                cancellationToken))
            {
                if (chunk.Type == "content" && !string.IsNullOrEmpty(chunk.Content) && assistantMessageId.HasValue)
                {
                    assistantContentBuilder.Append(chunk.Content);
                    try
                    {
                        await _telemetryLogger.AppendAssistantChunkAsync(
                            assistantMessageId.Value,
                            chunk.Content,
                            ct: cancellationToken
                        );
                    }
                    catch (Exception telemetryEx)
                    {
                        _logger.LogError(telemetryEx, "Failed to append streaming chunk for message {MessageId}", assistantMessageId.Value);
                    }
                }
                else if (chunk.Type == "metadata" && chunk.Metadata != null)
                {
                    if (TryGetString(chunk.Metadata, "intent", out var intentValue))
                    {
                        detectedIntent = intentValue;
                    }

                    if (TryGetString(chunk.Metadata, "sql", out var sqlValue))
                    {
                        generatedSql = sqlValue;
                    }

                    if (TryGetInt(chunk.Metadata, "result_count", out var resultValue))
                    {
                        resultCount = resultValue;
                    }

                    if (TryGetInt(chunk.Metadata, "total_latency_ms", out var latencyValue))
                    {
                        totalLatencyMs = latencyValue;
                    }
                }
                else if (chunk.Type == "error")
                {
                    errorMessage ??= chunk.Error ?? "An error occurred while processing your query. Please try again.";

                    if (!string.IsNullOrEmpty(chunk.Error) && assistantMessageId.HasValue)
                    {
                        assistantContentBuilder.Append(chunk.Error);
                        try
                        {
                            await _telemetryLogger.AppendAssistantChunkAsync(
                                assistantMessageId.Value,
                                chunk.Error,
                                ct: cancellationToken
                            );
                        }
                        catch (Exception telemetryEx)
                        {
                            _logger.LogError(telemetryEx, "Failed to append error chunk for message {MessageId}", assistantMessageId.Value);
                        }
                    }
                }

                // Convert to DTO
                var chunkDto = new StreamingChunkDto
                {
                    Type = chunk.Type,
                    Content = chunk.Content,
                    Metadata = chunk.Metadata,
                    Error = chunk.Error
                };

                // Serialize to JSON
                var json = JsonSerializer.Serialize(chunkDto, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                // Write SSE format: "data: {json}\n\n"
                await Response.WriteAsync($"data: {json}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);

                _logger.LogDebug("Sent streaming chunk: {Type}", chunk.Type);
            }

            _logger.LogInformation("Streaming query completed for user {UserId}", callerUserId);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Streaming query cancelled by client");
            errorMessage ??= "Streaming query cancelled by client.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error streaming chat query: {Query}", request.Query);

            errorMessage ??= "An error occurred while processing your query. Please try again.";

            if (assistantMessageId.HasValue)
            {
                assistantContentBuilder.Append(errorMessage);
                try
                {
                    await _telemetryLogger.AppendAssistantChunkAsync(
                        assistantMessageId.Value,
                        errorMessage,
                        ct: CancellationToken.None
                    );
                }
                catch (Exception telemetryEx)
                {
                    _logger.LogError(telemetryEx, "Failed to append catch-all error chunk for message {MessageId}", assistantMessageId.Value);
                }
            }

            // Send error chunk in SSE format
            var errorChunk = new StreamingChunkDto
            {
                Type = "error",
                Error = "An error occurred while processing your query. Please try again."
            };

            var json = JsonSerializer.Serialize(errorChunk, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            await Response.WriteAsync($"data: {json}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
        finally
        {
            if (assistantMessageId.HasValue)
            {
                try
                {
                    await _telemetryLogger.FinalizeAssistantMessageAsync(
                        assistantMessageId.Value,
                        assistantContentBuilder.ToString(),
                        intent: detectedIntent,
                        sqlGenerated: generatedSql,
                        resultRows: resultCount,
                        latencyMs: totalLatencyMs,
                        errorMessage: errorMessage,
                        ct: CancellationToken.None
                    );
                }
                catch (Exception telemetryEx)
                {
                    _logger.LogError(telemetryEx, "Failed to finalize streaming telemetry for session {SessionId}", sessionId);
                }
            }
        }
    }

    private static bool TryGetString(Dictionary<string, object> metadata, string key, out string? value)
    {
        if (metadata.TryGetValue(key, out var rawValue))
        {
            switch (rawValue)
            {
                case string str:
                    value = str;
                    return true;
                case JsonElement element when element.ValueKind == JsonValueKind.String:
                    value = element.GetString();
                    return true;
                default:
                    value = rawValue?.ToString();
                    return value != null;
            }
        }

        value = null;
        return false;
    }

    private bool TryResolveCallerUserId(out Guid callerUserId, out ActionResult? failureResult)
    {
        callerUserId = Guid.Empty;
        failureResult = null;

        var claim = User?.FindFirst(ClaimTypes.NameIdentifier)
                    ?? User?.FindFirst("sub")
                    ?? User?.FindFirst("user_id");

        if (claim == null)
        {
            failureResult = Unauthorized(new ProblemDetails
            {
                Status = 401,
                Title = "Unauthorized",
                Detail = "Authenticated user identity could not be resolved."
            });

            return false;
        }

        if (!Guid.TryParse(claim.Value, out callerUserId))
        {
            failureResult = Forbid();
            return false;
        }

        return true;
    }

    private static bool TryGetInt(Dictionary<string, object> metadata, string key, out int value)
    {
        if (metadata.TryGetValue(key, out var rawValue))
        {
            switch (rawValue)
            {
                case int intValue:
                    value = intValue;
                    return true;
                case long longValue:
                    value = (int)longValue;
                    return true;
                case double doubleValue:
                    value = (int)doubleValue;
                    return true;
                case JsonElement element when element.TryGetInt32(out var elementValue):
                    value = elementValue;
                    return true;
                default:
                    if (int.TryParse(rawValue?.ToString(), out var parsed))
                    {
                        value = parsed;
                        return true;
                    }

                    break;
            }
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Create a new chat session
    /// </summary>
    /// <param name="request">Session creation request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Created session details</returns>
    [HttpPost("session")]
    [ProducesResponseType(typeof(CreateSessionResponse), 201)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 500)]
    public async Task<ActionResult<CreateSessionResponse>> CreateSessionAsync(
        [FromBody] CreateSessionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            // SECURITY: Resolve caller identity from JWT only (not from request body)
            if (!TryResolveCallerUserId(out var callerUserId, out var authFailure))
            {
                return authFailure!;
            }

            var session = await _telemetryLogger.CreateSessionAsync(
                callerUserId,
                request.TtlMinutes,
                ct: cancellationToken
            );

            var response = new CreateSessionResponse
            {
                SessionId = session.Id,
                UserId = session.UserId,
                CreatedAt = session.StartedAt,
                ExpiresAt = session.ExpiresAt
            };

            _logger.LogInformation("Created session {SessionId} for user {UserId}", session.Id, session.UserId);

            // Return 201 Created with Location header pointing to the history endpoint
            Response.Headers.Append("Location", $"/api/Chat/session/{session.Id}/history");
            return StatusCode(201, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating session for user");
            return StatusCode(500, new ProblemDetails
            {
                Status = 500,
                Title = "Internal Server Error",
                Detail = "An error occurred while creating the session. Please try again."
            });
        }
    }

    /// <summary>
    /// Get conversation history for a session
    /// </summary>
    /// <param name="sessionId">Session ID</param>
    /// <param name="maxMessages">Maximum number of messages to retrieve (default: 20)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Conversation history</returns>
    [HttpGet("session/{sessionId}/history")]
    [ProducesResponseType(typeof(GetHistoryResponse), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 500)]
    public async Task<ActionResult<GetHistoryResponse>> GetHistoryAsync(
        [FromRoute] Guid sessionId,
        [FromQuery] int maxMessages = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!TryResolveCallerUserId(out var callerUserId, out var authFailure))
            {
                return authFailure!;
            }

            var session = await _telemetryLogger.GetSessionAsync(sessionId, cancellationToken);
            if (session == null || session.ExpiresAt <= DateTime.UtcNow)
            {
                return NotFound(new ProblemDetails
                {
                    Status = 404,
                    Title = "Session Not Found",
                    Detail = $"Session {sessionId} not found or has expired."
                });
            }

            if (session.UserId != callerUserId)
            {
                _logger.LogWarning(
                    "User {UserId} attempted to access session {SessionId} owned by {OwnerId}",
                    callerUserId,
                    sessionId,
                    session.UserId
                );

                return Forbid();
            }

            // Get history
            var messages = await _telemetryLogger.GetSessionHistoryAsync(
                sessionId,
                maxMessages,
                ct: cancellationToken
            );

            var response = new GetHistoryResponse
            {
                SessionId = sessionId,
                Messages = messages.Select(m => new ChatMessageDto
                {
                    Id = m.Id,
                    Role = m.Role,
                    Content = m.Content,
                    Intent = m.Intent,
                    CreatedAt = m.CreatedAt
                }).ToList()
            };

            _logger.LogInformation("Retrieved {Count} messages for session {SessionId}", response.Messages.Count, sessionId);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving history for session {SessionId}", sessionId);
            return StatusCode(500, new ProblemDetails
            {
                Status = 500,
                Title = "Internal Server Error",
                Detail = "An error occurred while retrieving the conversation history. Please try again."
            });
        }
    }

    /// <summary>
    /// Submit user feedback for a chat message (thumbs up/down, ratings, comments)
    /// </summary>
    /// <param name="request">Feedback submission request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Confirmation of feedback submission</returns>
    [HttpPost("feedback")]
    [ProducesResponseType(typeof(SubmitFeedbackResponse), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 500)]
    public async Task<ActionResult<SubmitFeedbackResponse>> SubmitFeedbackAsync(
        [FromBody] SubmitFeedbackRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!TryResolveCallerUserId(out var callerUserId, out var authFailure))
            {
                return authFailure!;
            }

            _logger.LogInformation(
                "Received feedback from user {UserId} for message {MessageId}: {FeedbackType}",
                callerUserId,
                request.MessageId,
                request.FeedbackType
            );

            var messageSession = await _telemetryLogger.GetSessionByMessageIdAsync(
                request.MessageId,
                cancellationToken
            );

            if (messageSession == null)
            {
                return NotFound(new ProblemDetails
                {
                    Status = 404,
                    Title = "Message Not Found",
                    Detail = $"Message {request.MessageId} not found."
                });
            }

            if (request.SessionId.HasValue && request.SessionId.Value != messageSession.Id)
            {
                return BadRequest(new ProblemDetails
                {
                    Status = 400,
                    Title = "Session Mismatch",
                    Detail = "The provided session does not match the message being rated."
                });
            }

            if (messageSession.ExpiresAt <= DateTime.UtcNow)
            {
                return NotFound(new ProblemDetails
                {
                    Status = 404,
                    Title = "Session Not Found",
                    Detail = $"Session {messageSession.Id} not found or has expired."
                });
            }

            if (messageSession.UserId != callerUserId)
            {
                _logger.LogWarning(
                    "User {UserId} attempted to submit feedback for session {SessionId} owned by {OwnerId}",
                    callerUserId,
                    messageSession.Id,
                    messageSession.UserId
                );

                return Forbid();
            }

            // Log feedback to database
            var feedback = await _telemetryLogger.LogFeedbackAsync(
                messageSession.Id,
                request.MessageId,
                callerUserId,
                request.FeedbackType,
                request.Rating,
                request.Comment,
                ct: cancellationToken
            );

            var response = new SubmitFeedbackResponse
            {
                FeedbackId = feedback.Id,
                MessageId = feedback.MessageId,
                SessionId = feedback.SessionId,
                FeedbackType = feedback.FeedbackType,
                SubmittedAt = feedback.CreatedAt
            };

            _logger.LogInformation(
                "Feedback {FeedbackId} submitted successfully for message {MessageId}",
                feedback.Id,
                request.MessageId
            );

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error submitting feedback for message {MessageId}",
                request.MessageId
            );

            return StatusCode(500, new ProblemDetails
            {
                Status = 500,
                Title = "Internal Server Error",
                Detail = "An error occurred while submitting your feedback. Please try again."
            });
        }
    }

    /// <summary>
    /// Health check endpoint for chat service
    /// </summary>
    /// <returns>Service status</returns>
    [HttpGet("health")]
    [ProducesResponseType(200)]
    public IActionResult Health()
    {
        return Ok(new
        {
            status = "healthy",
            service = "chat",
            timestamp = DateTime.UtcNow
        });
    }
}
