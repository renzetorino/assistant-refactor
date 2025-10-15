using dataAccess.Api.Services;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace dataAccess.Api.Services;

/// <summary>
/// Orchestrates the complete query pipeline:
/// Question -> SQL Generation (70B) -> Validation -> Execution -> Summarization (8B) -> Response Formatting
/// </summary>
public class QueryPipeline
{
    private readonly LlmSqlGenerator _sqlGenerator;
    private readonly SqlValidator _validator;
    private readonly VirtualTableRewriter _rewriter;
    private readonly LlmSummarizer _summarizer;
    private readonly ResponseFormatter _formatter;
    private readonly string _connectionString;
    private readonly ILogger<QueryPipeline> _logger;

    public QueryPipeline(
        LlmSqlGenerator sqlGenerator,
        SqlValidator validator,
        VirtualTableRewriter rewriter,
        LlmSummarizer summarizer,
        ResponseFormatter formatter,
        IConfiguration configuration,
        ILogger<QueryPipeline> logger)
    {
        _sqlGenerator = sqlGenerator;
        _validator = validator;
        _rewriter = rewriter;
        _summarizer = summarizer;
        _formatter = formatter;
        _logger = logger;
        
        // Get connection string from environment or config
        _connectionString = Environment.GetEnvironmentVariable("APP__REL__CONNECTIONSTRING")
            ?? configuration["APP__REL__CONNECTIONSTRING"]
            ?? throw new InvalidOperationException("APP__REL__CONNECTIONSTRING is not configured");
    }

    /// <summary>
    /// Executes the complete query pipeline and returns a formatted response.
    /// </summary>
    public async Task<object> ExecuteAsync(string question, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("=== Pipeline Start === Question: {Question}", question);

            // Step 1: Generate SQL using LLM (70B model)
            _logger.LogInformation("Step 1: Generating SQL...");
            var generatedSql = await _sqlGenerator.GenerateSqlAsync(question, ct);
            
            if (string.IsNullOrWhiteSpace(generatedSql))
            {
                _logger.LogWarning("SQL generation failed");
                return _formatter.FormatError(question, "Unable to generate SQL query from your question.");
            }

            _logger.LogInformation("Generated SQL: {SQL}", generatedSql);

            // Step 2: Validate SQL
            _logger.LogInformation("Step 2: Validating SQL...");
            var (isValid, errorMessage) = _validator.ValidateSql(generatedSql);
            
            if (!isValid)
            {
                _logger.LogWarning("Validation failed: {Error}", errorMessage);
                return _formatter.FormatValidationError(question, generatedSql, errorMessage ?? "SQL validation failed");
            }

            _logger.LogInformation("SQL validated successfully");

            // Step 3: Rewrite virtual tables (e.g., sales view)
            _logger.LogInformation("Step 3: Rewriting virtual tables...");
            var rewrittenSql = _rewriter.RewriteIfNeeded(generatedSql);
            _logger.LogInformation("Rewritten SQL: {SQL}", rewrittenSql);

            // Step 4: Execute SQL
            _logger.LogInformation("Step 4: Executing SQL...");
            var (results, rowCount) = await ExecuteSqlAsync(rewrittenSql, ct);
            _logger.LogInformation("Query executed: {RowCount} rows returned", rowCount);

            // Step 5: Generate summary using LLM (8B model)
            _logger.LogInformation("Step 5: Generating summary...");
            var summary = await _summarizer.SummarizeAsync(question, generatedSql, results, rowCount, ct);
            _logger.LogInformation("Summary generated: {Summary}", summary);

            // Step 6: Format response
            _logger.LogInformation("Step 6: Formatting response...");
            var response = _formatter.FormatSuccess(
                question,
                generatedSql,
                rowCount,
                summary,
                results
            );

            _logger.LogInformation("=== Pipeline Complete ===");
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pipeline error");
            return _formatter.FormatError(question, $"An error occurred: {ex.Message}");
        }
    }

    /// <summary>
    /// Executes SQL and returns results as JSON.
    /// </summary>
    private async Task<(JsonElement results, int rowCount)> ExecuteSqlAsync(string sql, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = 30;

        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var rows = new List<Dictionary<string, object?>>();
        
        while (await reader.ReadAsync(ct))
        {
            var row = new Dictionary<string, object?>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var colName = reader.GetName(i);
                var val = reader.IsDBNull(i) ? null : reader.GetValue(i);
                row[colName] = val;
            }
            rows.Add(row);
        }

        var json = JsonSerializer.Serialize(rows);
        var doc = JsonDocument.Parse(json);
        
        return (doc.RootElement, rows.Count);
    }
}
