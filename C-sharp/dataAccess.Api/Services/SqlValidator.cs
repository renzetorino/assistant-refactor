using Shared.Allowlists;
using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace dataAccess.Api.Services;

/// <summary>
/// Validates SQL queries against allowlist rules to ensure they are safe to execute.
/// </summary>
public class SqlValidator
{
    private readonly ISqlAllowlist _allowlist;
    private readonly ILogger<SqlValidator> _logger;

    // Dangerous SQL keywords that should never appear
    private static readonly HashSet<string> DangerousKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "INSERT", "UPDATE", "DELETE", "DROP", "ALTER", "CREATE", "TRUNCATE",
        "EXEC", "EXECUTE", "GRANT", "REVOKE", "MERGE", "CALL",
        "REPLACE", "RENAME", "COMMENT", "COPY"
    };

    public SqlValidator(ISqlAllowlist allowlist, ILogger<SqlValidator> logger)
    {
        _allowlist = allowlist;
        _logger = logger;
    }

    /// <summary>
    /// Validates a SQL query for safety.
    /// Returns (isValid, errorMessage).
    /// </summary>
    public (bool isValid, string? errorMessage) ValidateSql(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return (false, "SQL query is empty");

        var normalized = sql.Trim().ToUpperInvariant();

        // 1. Must start with SELECT
        if (!normalized.StartsWith("SELECT"))
            return (false, "Only SELECT queries are allowed");

        // 2. Check for dangerous keywords
        foreach (var keyword in DangerousKeywords)
        {
            // Use word boundaries to avoid false positives (e.g., "SELECT" contains "ELECT")
            var pattern = $@"\b{Regex.Escape(keyword)}\b";
            if (Regex.IsMatch(normalized, pattern, RegexOptions.IgnoreCase))
            {
                _logger.LogWarning("Dangerous keyword detected: {Keyword}", keyword);
                return (false, $"Dangerous keyword not allowed: {keyword}");
            }
        }

        // 3. Check for semicolons (potential SQL injection with multiple statements)
        if (sql.Count(c => c == ';') > 1)
            return (false, "Multiple SQL statements not allowed");

        // 4. Check for comment injection (-- or /* */)
        if (sql.Contains("--") || sql.Contains("/*"))
        {
            _logger.LogWarning("SQL comment detected, potential injection attempt");
            return (false, "SQL comments not allowed");
        }

        // 5. Extract and validate table names
        var tables = ExtractTableNames(sql);
        foreach (var table in tables)
        {
            if (!_allowlist.IsTableAllowed(table))
            {
                _logger.LogWarning("Unauthorized table access attempt: {Table}", table);
                return (false, $"Table not allowed: {table}");
            }
        }

        // 6. Basic column validation (simplified - checks common patterns)
        // Note: This is not exhaustive but catches most cases
        var result = ValidateColumns(sql);
        if (!result.isValid)
            return result;

        _logger.LogInformation("SQL validation passed");
        return (true, null);
    }

    /// <summary>
    /// Extracts table names from FROM and JOIN clauses.
    /// Uses Microsoft ScriptDom SQL parser with fallback to regex.
    /// </summary>
    private HashSet<string> ExtractTableNames(string sql)
    {
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        try
        {
            // Use TSql160Parser for modern SQL Server/PostgreSQL-compatible parsing
            var parser = new TSql160Parser(true);
            IList<ParseError> errors;
            
            using var reader = new StringReader(sql);
            var fragment = parser.Parse(reader, out errors);
            
            // If there are critical parsing errors, fall back to regex
            if (errors.Any(e => e.Number >= 46000))
            {
                _logger.LogWarning("SQL parsing had critical errors, falling back to regex");
                return ExtractTableNamesRegex(sql);
            }
            
            // Extract tables using a visitor pattern
            var visitor = new TableNameVisitor();
            fragment.Accept(visitor);
            
            foreach (var table in visitor.TableNames)
            {
                tables.Add(table);
            }
            
            return tables;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse SQL for table extraction, falling back to regex");
            return ExtractTableNamesRegex(sql);
        }
    }

    /// <summary>
    /// Fallback regex-based table extraction.
    /// </summary>
    private HashSet<string> ExtractTableNamesRegex(string sql)
    {
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        // Match FROM table_name
        var fromMatches = Regex.Matches(sql, @"\bFROM\s+([a-zA-Z_][a-zA-Z0-9_]*)", RegexOptions.IgnoreCase);
        foreach (Match match in fromMatches)
        {
            if (match.Groups.Count > 1)
                tables.Add(match.Groups[1].Value);
        }

        // Match JOIN table_name
        var joinMatches = Regex.Matches(sql, @"\bJOIN\s+([a-zA-Z_][a-zA-Z0-9_]*)", RegexOptions.IgnoreCase);
        foreach (Match match in joinMatches)
        {
            if (match.Groups.Count > 1)
                tables.Add(match.Groups[1].Value);
        }

        return tables;
    }

    /// <summary>
    /// Visitor to extract table names from SQL AST.
    /// </summary>
    private class TableNameVisitor : TSqlFragmentVisitor
    {
        public HashSet<string> TableNames { get; } = new(StringComparer.OrdinalIgnoreCase);

        public override void ExplicitVisit(NamedTableReference node)
        {
            // Extract the table name from the SchemaObject
            if (node.SchemaObject?.BaseIdentifier?.Value != null)
            {
                TableNames.Add(node.SchemaObject.BaseIdentifier.Value);
            }
            
            base.ExplicitVisit(node);
        }
    }

    /// <summary>
    /// Validates column references in the query.
    /// Simplified check - validates common SELECT patterns.
    /// </summary>
    private (bool isValid, string? errorMessage) ValidateColumns(string sql)
    {
        // Extract table names first
        var tables = ExtractTableNames(sql);
        
        // Match table.column patterns
        var columnMatches = Regex.Matches(sql, @"([a-zA-Z_][a-zA-Z0-9_]*)\.([a-zA-Z_][a-zA-Z0-9_]*)", RegexOptions.IgnoreCase);
        
        foreach (Match match in columnMatches)
        {
            if (match.Groups.Count > 2)
            {
                var table = match.Groups[1].Value;
                var column = match.Groups[2].Value;

                // Skip if it's not a table we extracted (might be an alias)
                if (!tables.Contains(table))
                    continue;

                if (!_allowlist.IsColumnAllowed(table, column))
                {
                    _logger.LogWarning("Unauthorized column access: {Table}.{Column}", table, column);
                    return (false, $"Column not allowed: {table}.{column}");
                }
            }
        }

        return (true, null);
    }

    /// <summary>
    /// Checks if the query has a reasonable LIMIT clause to prevent large result sets.
    /// </summary>
    public bool HasReasonableLimit(string sql)
    {
        var limitMatch = Regex.Match(sql, @"\bLIMIT\s+(\d+)", RegexOptions.IgnoreCase);
        if (limitMatch.Success && int.TryParse(limitMatch.Groups[1].Value, out var limit))
        {
            return limit <= _allowlist.MaxLimit;
        }
        
        // No LIMIT clause - we should add one
        return false;
    }

    /// <summary>
    /// Adds a LIMIT clause if missing or exceeds max.
    /// </summary>
    public string EnsureLimit(string sql, int maxLimit)
    {
        if (!HasReasonableLimit(sql))
        {
            // Remove existing LIMIT if present
            sql = Regex.Replace(sql, @"\bLIMIT\s+\d+", "", RegexOptions.IgnoreCase);
            
            // Add reasonable LIMIT
            sql = sql.TrimEnd(';', ' ', '\n', '\r');
            sql += $" LIMIT {Math.Min(maxLimit, _allowlist.MaxLimit)}";
        }
        
        return sql;
    }
}
