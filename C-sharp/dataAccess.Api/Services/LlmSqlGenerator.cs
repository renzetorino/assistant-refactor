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

    public LlmSqlGenerator(
        GroqJsonClient groq,
        ISqlAllowlist allowlist,
        LlmSqlPromptLoader promptLoader,
        ILogger<LlmSqlGenerator> logger)
    {
        _groq = groq;
        _allowlist = allowlist;
        _promptLoader = promptLoader;
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
            var systemPrompt = BuildSystemPrompt();
            var userPrompt = BuildUserPrompt(userQuestion);

            _logger.LogInformation("Generating SQL for question: {Question}", userQuestion);

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

            _logger.LogWarning("LLM response did not contain SQL query");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating SQL from LLM");
            return null;
        }
    }

    private string BuildSystemPrompt()
    {
        var config = _promptLoader.LoadConfig();
        
        // Build detailed schema section with columns if allowlist supports it
        var schemaBuilder = new StringBuilder();
        
        // Check if allowlist has column info (SqlAllowlistV2 has IsColumnAllowed method)
        var hasColumnInfo = _allowlist is SqlAllowlistV2;
        
        if (hasColumnInfo)
        {
            // Build detailed schema with columns
            var detailedSchema = BuildDetailedSchema();
            var schema = config.SchemaTemplate.Replace("{tables}", detailedSchema);
            var relationships = config.RelationshipsTemplate;
            
            return config.SystemPrompt
                .Replace("{schema}", schema)
                .Replace("{relationships}", relationships);
        }
        else
        {
            // Fallback: just table names
            foreach (var table in _allowlist.Tables)
            {
                schemaBuilder.AppendLine($"  - {table}");
            }
            
            var schema = config.SchemaTemplate.Replace("{tables}", schemaBuilder.ToString().TrimEnd());
            var relationships = config.RelationshipsTemplate;
            
            return config.SystemPrompt
                .Replace("{schema}", schema)
                .Replace("{relationships}", relationships);
        }
    }

    private string BuildDetailedSchema()
    {
        var sb = new StringBuilder();
        
        // Define column mappings for key tables (from SqlAllowlistV2)
        var tableColumns = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["products"] = new[] { "productid", "productname", "description", "supplierid", "createdat", "updatedat", "image_url" },
            ["suppliers"] = new[] { "supplierid", "suppliername", "contactperson", "phonenumber", "supplieremail", "address", "createdat", "updatedat", "supplierstatus" },
            ["productcategory"] = new[] { "productcategoryid", "productid", "price", "cost", "color", "agesize", "currentstock", "reorderpoint", "updatedstock" },
            ["orders"] = new[] { "orderid", "orderdate", "totalamount", "orderstatus", "createdat", "updatedat", "amount_paid", "change" },
            ["orderitems"] = new[] { "orderitemid", "orderid", "productid", "productcategoryid", "quantity", "unitprice", "subtotal", "createdat", "updatedat" },
            ["defectiveitems"] = new[] { "defectiveitemid", "productid", "productcategoryid", "reporteddate", "defectdescription", "quantity", "status", "createdat", "updatedat" },
            ["expenses"] = new[] { "id", "user_id", "occurred_on", "category_id", "amount", "notes", "status", "contact_id", "created_at", "updated_at" },
            ["categories"] = new[] { "id", "user_id", "name", "is_active", "created_at", "updated_at" },
            ["contacts"] = new[] { "id", "user_id", "name", "phone", "email", "address", "note", "created_at", "updated_at" },
            ["labels"] = new[] { "id", "user_id", "name", "color", "created_at" },
            ["expense_labels"] = new[] { "expense_id", "label_id" },
            ["budget"] = new[] { "id", "month_year", "monthly_budget_amount", "created_at" },
            ["budgethistory"] = new[] { "id", "budget_id", "old_amount", "new_amount", "created_at" },
            ["attachments"] = new[] { "id", "user_id", "expense_id", "storage_key", "mime_type", "size_bytes", "uploaded_at", "created_at" },
            ["planned_payments"] = new[] { "id", "user_id", "name", "category_id", "amount", "contact_id", "frequency", "due_date", "notes", "label_id", "created_at", "updated_at" },
            ["planned_recurrence"] = new[] { "planned_payment_id", "repeat", "every", "duration", "until_date", "occurrences_count" },
            ["sales"] = new[] { "orderid", "orderdate", "productid", "productname", "quantity", "unitprice", "subtotal", "revenue", "profit" }
        };
        
        foreach (var table in _allowlist.Tables.OrderBy(t => t))
        {
            if (tableColumns.TryGetValue(table, out var columns))
            {
                sb.AppendLine($"  - {table} ({string.Join(", ", columns)})");
            }
            else
            {
                sb.AppendLine($"  - {table}");
            }
        }
        
        return sb.ToString().TrimEnd();
    }

    private string BuildUserPrompt(string userQuestion)
    {
        var config = _promptLoader.LoadConfig();
        return config.UserPrompt.Replace("{question}", userQuestion);
    }
}
