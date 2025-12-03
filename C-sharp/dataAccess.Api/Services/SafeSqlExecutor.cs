using Npgsql;
using System.Data;

namespace dataAccess.Api.Services;

/// <summary>
/// Executes validated SQL queries safely with read-only access and defensive limits.
/// </summary>
public interface ISafeSqlExecutor
{
    Task<List<Dictionary<string, object?>>> ExecuteQueryAsync(string sql, CancellationToken ct = default);
    Task<object> ExecuteAndFormatAsync(string sql, string? explanation = null, CancellationToken ct = default);
    string FormatAsMarkdown(List<Dictionary<string, object?>> rows, string? explanation = null);
}

/// <inheritdoc cref="ISafeSqlExecutor" />
public class SafeSqlExecutor : ISafeSqlExecutor
{
    private readonly string _connectionString;
    private readonly ILogger<SafeSqlExecutor> _logger;
    private readonly int _commandTimeoutSeconds;
    private readonly int _maxRows;

    public SafeSqlExecutor(IConfiguration configuration, ILogger<SafeSqlExecutor> logger)
    {
        _logger = logger;

        var configuredReadOnly = configuration.GetConnectionString("DefaultConnectionReadOnly")
                                 ?? configuration["ConnectionStrings:DefaultConnectionReadOnly"];

        if (string.IsNullOrWhiteSpace(configuredReadOnly))
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? configuration["APP__REL__CONNECTIONSTRING"]
                ?? Environment.GetEnvironmentVariable("APP__REL__CONNECTIONSTRING")
                ?? throw new InvalidOperationException("Read-only connection string is not configured. Set ConnectionStrings:DefaultConnectionReadOnly to a read-only user.");

            _logger.LogWarning("DefaultConnectionReadOnly missing; falling back to DefaultConnection. Ensure this credential is read-only.");
        }
        else
        {
            _connectionString = configuredReadOnly;
        }

        _commandTimeoutSeconds = configuration.GetValue<int?>("SqlExecution:CommandTimeoutSeconds") ?? 30;
        _maxRows = configuration.GetValue<int?>("SqlExecution:MaxRows") ?? 1000;
    }

    /// <inheritdoc />
    public async Task<List<Dictionary<string, object?>>> ExecuteQueryAsync(string sql, CancellationToken ct = default)
    {
        var results = new List<Dictionary<string, object?>>();

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(ct);

            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

            // Force the transaction into read-only mode even if the user credential is misconfigured.
            await using (var setReadOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction))
            {
                await setReadOnly.ExecuteNonQueryAsync(ct);
            }

            await using var command = new NpgsqlCommand(sql, connection, transaction)
            {
                CommandTimeout = _commandTimeoutSeconds
            };

            await using var reader = await command.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                var row = new Dictionary<string, object?>();

                for (var i = 0; i < reader.FieldCount; i++)
                {
                    var columnName = reader.GetName(i);
                    var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    row[columnName] = value;
                }

                results.Add(row);

                if (results.Count >= _maxRows)
                {
                    _logger.LogWarning("Query returned more than {MaxRows} rows. Truncating result set.", _maxRows);
                    break;
                }
            }

            await transaction.CommitAsync(ct);

            _logger.LogInformation("Query executed successfully with {RowCount} rows.", results.Count);
            return results;
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "PostgreSQL error during query execution: {Sql}", sql);
            throw new InvalidOperationException("The database rejected the query execution.", ex);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("SQL execution cancelled for query: {Sql}", sql);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error executing SQL query: {Sql}", sql);
            throw new InvalidOperationException("SQL execution failed.", ex);
        }
    }

    /// <inheritdoc />
    public async Task<object> ExecuteAndFormatAsync(string sql, string? explanation = null, CancellationToken ct = default)
    {
        try
        {
            var rows = await ExecuteQueryAsync(sql, ct);

            return new
            {
                success = true,
                rowCount = rows.Count,
                data = rows,
                sql,
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
                sql,
                executedAt = DateTime.UtcNow
            };
        }
    }

    /// <inheritdoc />
    public string FormatAsMarkdown(List<Dictionary<string, object?>> rows, string? explanation = null)
    {
        if (rows.Count == 0)
        {
            return !string.IsNullOrWhiteSpace(explanation)
                ? $"{explanation}\n\nNo results found."
                : "No results found.";
        }

        var sb = new System.Text.StringBuilder();

        if (!string.IsNullOrWhiteSpace(explanation))
        {
            sb.AppendLine(explanation);
            sb.AppendLine();
        }

        var columns = rows[0].Keys.ToList();

        sb.Append("| ");
        sb.AppendJoin(" | ", columns);
        sb.AppendLine(" |");

        sb.Append("| ");
        sb.AppendJoin(" | ", columns.Select(_ => "---"));
        sb.AppendLine(" |");

        foreach (var row in rows.Take(50))
        {
            sb.Append("| ");
            sb.AppendJoin(" | ", columns.Select(col =>
            {
                var value = row[col];
                if (value == null) return "NULL";
                if (value is DateTime dt) return dt.ToString("yyyy-MM-dd HH:mm");
                if (value is DateOnly d) return d.ToString("yyyy-MM-dd");
                if (value is decimal dec) return dec.ToString("N2");
                return value.ToString() ?? string.Empty;
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
