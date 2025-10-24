using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Data;
using System.Text.Json;

namespace dataAccess.Api.Services;

/// <summary>
/// Executes validated SQL queries safely with read-only access.
/// </summary>
public class SafeSqlExecutor
{
    private readonly string _connectionString;
    private readonly ILogger<SafeSqlExecutor> _logger;

    public SafeSqlExecutor(IConfiguration configuration, ILogger<SafeSqlExecutor> logger)
    {
        _connectionString = Environment.GetEnvironmentVariable("APP__REL__CONNECTIONSTRING")
                          ?? configuration["APP__REL__CONNECTIONSTRING"]
                          ?? throw new Exception("APP__REL__CONNECTIONSTRING missing");
        _logger = logger;
    }

    /// <summary>
    /// Executes a SELECT query and returns results as a list of dictionaries.
    /// </summary>
    public async Task<List<Dictionary<string, object?>>> ExecuteQueryAsync(
        string sql, 
        CancellationToken ct = default)
    {
        var results = new List<Dictionary<string, object?>>();

        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            await using var cmd = new NpgsqlCommand(sql, conn)
            {
                CommandTimeout = 30 // 30 second timeout
            };

            await using var reader = await cmd.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                var row = new Dictionary<string, object?>();
                
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var columnName = reader.GetName(i);
                    var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    row[columnName] = value;
                }
                
                results.Add(row);
            }

            _logger.LogInformation("Query executed successfully, returned {RowCount} rows", results.Count);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing SQL query: {Sql}", sql);
            throw;
        }
    }

    /// <summary>
    /// Executes a query and returns results formatted for the frontend.
    /// </summary>
    public async Task<object> ExecuteAndFormatAsync(
        string sql, 
        string? explanation = null,
        CancellationToken ct = default)
    {
        try
        {
            var rows = await ExecuteQueryAsync(sql, ct);

            return new
            {
                success = true,
                rowCount = rows.Count,
                data = rows,
                sql = sql,
                explanation = explanation ?? "Query executed successfully",
                executedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Query execution failed");
            return new
            {
                success = false,
                error = ex.Message,
                sql = sql,
                executedAt = DateTime.UtcNow
            };
        }
    }

    /// <summary>
    /// Formats query results as markdown table for chat display.
    /// </summary>
    public string FormatAsMarkdown(List<Dictionary<string, object?>> rows, string? explanation = null)
    {
        if (rows.Count == 0)
            return explanation != null 
                ? $"{explanation}\n\nNo results found." 
                : "No results found.";

        var sb = new System.Text.StringBuilder();
        
        if (!string.IsNullOrWhiteSpace(explanation))
        {
            sb.AppendLine(explanation);
            sb.AppendLine();
        }

        // Get column names from first row
        var columns = rows[0].Keys.ToList();

        // Header
        sb.Append("| ");
        sb.AppendJoin(" | ", columns);
        sb.AppendLine(" |");

        // Separator
        sb.Append("| ");
        sb.AppendJoin(" | ", columns.Select(_ => "---"));
        sb.AppendLine(" |");

        // Rows (limit to first 50 for display)
        var displayRows = rows.Take(50);
        foreach (var row in displayRows)
        {
            sb.Append("| ");
            sb.AppendJoin(" | ", columns.Select(col => 
            {
                var value = row[col];
                if (value == null) return "NULL";
                if (value is DateTime dt) return dt.ToString("yyyy-MM-dd HH:mm");
                if (value is DateOnly d) return d.ToString("yyyy-MM-dd");
                if (value is decimal dec) return dec.ToString("N2");
                return value.ToString() ?? "";
            }));
            sb.AppendLine(" |");
        }

        if (rows.Count > 50)
        {
            sb.AppendLine();
            sb.AppendLine($"*Showing first 50 of {rows.Count} results*");
        }

        return sb.ToString();
    }
}
