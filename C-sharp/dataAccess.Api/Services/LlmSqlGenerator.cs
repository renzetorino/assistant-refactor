using dataAccess.LLM;
using Shared.Allowlists;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
namespace dataAccess.Api.Services;

/// <summary>
/// Generates SQL queries from natural language using an LLM (Groq API).
/// Uses schema and allowlist context to ensure safe, valid queries.
/// Prompts are loaded from YAML configuration for easy maintenance.
/// </summary>
public class LlmSqlGenerator
{
    private readonly GroqJsonClient _groq;
    private readonly ISqlAllowlist _allowlist;
    private readonly ILogger<LlmSqlGenerator> _logger;
    private readonly LlmSqlPromptLoader _promptLoader;
    private readonly IDatabaseSchemaService _schemaService;

    public LlmSqlGenerator(
        GroqJsonClient groq,
        ISqlAllowlist allowlist,
        LlmSqlPromptLoader promptLoader,
        IDatabaseSchemaService schemaService,
        ILogger<LlmSqlGenerator> logger)
    {
        _groq = groq;
        _allowlist = allowlist;
        _promptLoader = promptLoader;
        _schemaService = schemaService;
        _logger = logger;
    }

    /// <summary>
    /// Generates a SQL query from a natural language question.
    /// Returns null if the LLM cannot generate a valid query.
    /// </summary>
    public async Task<string?> GenerateSqlAsync(string userQuestion, CancellationToken ct = default)
    {
        try
        {
            // ✅ FIX: Use filtered schema from DatabaseSchemaService (500-1000 tokens instead of 5000+)
            var filteredSchema = await _schemaService.GetRelevantSchemaAsync(userQuestion);
            
            var config = _promptLoader.LoadConfig();
            
            // Build system prompt with FILTERED schema
            var systemPrompt = config.SystemPrompt
                .Replace("{schema}", filteredSchema)
                .Replace("{relationships}", config.RelationshipsTemplate);

            var userPrompt = config.UserPrompt.Replace("{question}", userQuestion);

            _logger.LogInformation(
                "[LlmSqlGenerator] Generating SQL with FILTERED schema ({Length} chars) for: {Question}", 
                filteredSchema.Length, 
                userQuestion
            );

            // Use the 70B report model for more accurate SQL generation
            using var doc = await _groq.CompleteJsonAsyncReport(systemPrompt, userPrompt, null, 0.1, ct);
            
            if (doc.RootElement.TryGetProperty("sql", out var sqlEl) && 
                sqlEl.ValueKind == JsonValueKind.String)
            {
                var sql = sqlEl.GetString();
                _logger.LogInformation("Generated SQL: {Sql}", sql);
                return sql;
            }

            // Try alternative property names
            if (doc.RootElement.TryGetProperty("query", out var queryEl) && 
                queryEl.ValueKind == JsonValueKind.String)
            {
                var sql = queryEl.GetString();
                _logger.LogInformation("Generated SQL (from 'query'): {Sql}", sql);
                return sql;
            }

            _logger.LogWarning("[LlmSqlGenerator] LLM response did not contain SQL query");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LlmSqlGenerator] Error generating SQL from LLM");
            return null;
        }
    }

    // ❌ DELETED: BuildSystemPrompt() - Now uses filtered schema from DatabaseSchemaService
    // ❌ DELETED: BuildDetailedSchema() - Replaced by DatabaseSchemaService.GetRelevantSchemaAsync()
    // These methods caused token bloat by sending ALL 30+ tables (5000+ tokens) instead of filtered 3-5 tables (500-1000 tokens)
}
