using System;
using System.Collections.Generic;
using Shared.Allowlists;
using System.Text.RegularExpressions;

namespace dataAccess.Api.Services;

/// <summary>
/// Validates SQL queries against allowlist rules to ensure they are safe to execute.
/// Production-grade validator with parser-aware scanning and defense-in-depth protection.
/// </summary>
public class SqlValidator
{
    private readonly ISqlAllowlist _allowlist;
    private readonly ILogger<SqlValidator> _logger;

    // Dangerous SQL patterns that can modify database structure or data
    // These are checked AFTER stripping comments and string literals to avoid false positives
    private static readonly string[] DangerousPatterns = new[]
    {
        // Data Modification Language (DML) - modifies data
        @"\bINSERT\b", @"\bUPDATE\b", @"\bDELETE\b", @"\bMERGE\b",
        // Note: REPLACE is allowed as it's a common string function in PG/MySQL
        
        // Data Definition Language (DDL) - modifies structure
        @"\bDROP\b", @"\bALTER\b", @"\bCREATE\b", @"\bTRUNCATE\b", @"\bRENAME\b",
        
        // Data Control Language (DCL) - modifies permissions
        @"\bGRANT\b", @"\bREVOKE\b",
        
        // Stored Procedures and Functions - can execute arbitrary code
        @"\bEXEC(?:UTE)?\b", @"\bCALL\b",
        
        // Transaction Control - can interfere with connection state
        @"\bBEGIN\s+(?:WORK|TRANSACTION)\b", @"\bCOMMIT\b", @"\bROLLBACK\b", 
        @"\bSAVEPOINT\b", @"\bSTART\s+TRANSACTION\b",
        
        // Session/Schema manipulation
        @"\bSET\s+(?:ROLE|SESSION|search_path|TRANSACTION)\b", @"\bRESET\b",
        @"\bALTER\s+SYSTEM\b",
        
        // Maintenance operations
        @"\bVACUUM\b", @"\bANALYZE\b", @"\bREINDEX\b", @"\bCLUSTER\b",
        @"\bREFRESH\s+MATERIALIZED\s+VIEW\b",
        
        // Export/Import operations that can write files or execute code
        @"\bCOPY\s+(?:\w+|\()\s+(?:TO|FROM|PROGRAM)\b", @"\bCOPY\s+INTO\b",
        @"\bCOPY\s+\w+\s+TO\s+STDOUT\b", @"\bCOPY\s+\([\s\S]*?\)\s+TO\s+STDOUT\b",
        @"\bINTO\s+OUTFILE\b", @"\bINTO\s+DUMPFILE\b", @"\bUNLOAD\b",
        @"\bIMPORT\b", @"\bEXPORT\b", @"\bBACKUP\b", @"\bRESTORE\b",
        
        // Listen/Notify (can be used for side channels)
        @"\bLISTEN\b", @"\bNOTIFY\b", @"\bUNLISTEN\b",
        
        // LOAD extensions (PostgreSQL - can load arbitrary code)
        @"\bLOAD\b", @"\bLOAD\s+EXTENSION\b",
        
        // DO blocks (PostgreSQL anonymous code blocks)
        @"\bDO\s+\$\$", @"\bDO\s+LANGUAGE\b"
    };

    private static readonly Regex[] SqlInjectionPatterns = new[]
    {
        new Regex(@"\bOR\s+1\s*=\s*1\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\bOR\s*''\s*=\s*''", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\bOR\s+'?1'?\s*=\s*'?1'?", RegexOptions.IgnoreCase | RegexOptions.Compiled)
    };

    public SqlValidator(ISqlAllowlist allowlist, ILogger<SqlValidator> logger)
    {
        _allowlist = allowlist;
        _logger = logger;
    }

    /// <summary>
    /// Strips comments and string literals from SQL to prepare for keyword scanning.
    /// This prevents false positives from keywords appearing in strings/comments.
    /// IMPORTANT: Double-quoted identifiers are preserved (they're table/column names in PostgreSQL, not strings).
    /// </summary>
    private static string StripForScan(string sql)
    {
        // Remove block comments /* ... */
        sql = Regex.Replace(sql, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        
        // Remove line comments -- ...
        sql = Regex.Replace(sql, @"--[^\r\n]*", " ", RegexOptions.Multiline);
        
        // Remove dollar-quoted strings (PostgreSQL): $$...$$ or $tag$...$tag$
        // Use backreference to ensure opening and closing tags match
        sql = Regex.Replace(sql, @"\$([a-zA-Z_][a-zA-Z0-9_]*)?\$[\s\S]*?\$\1\$", "''", RegexOptions.Singleline);
        
        // Remove single-quoted strings (handle escaped quotes: '' inside strings)
        sql = Regex.Replace(sql, @"'(?:''|[^'])*'", "''");
        
        // Keep double-quoted identifiers intact in PostgreSQL; they are table/column names, not string literals.
        // DO NOT strip them - needed for SELECT INTO "table" detection and other validations.
        
        // Remove E-strings (PostgreSQL escape strings): E'...'
        sql = Regex.Replace(sql, @"E'(?:''|[^'])*'", "''", RegexOptions.IgnoreCase);
        
        return sql;
    }

    /// <summary>
    /// Extracts CTE (Common Table Expression) names from WITH clauses.
    /// CTEs should not be validated against the table allowlist.
    /// </summary>
    private HashSet<string> ExtractCteNames(string sql)
    {
        var cteNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        // Pattern: WITH cte_name AS (...) or WITH RECURSIVE cte_name AS (...)
        // Supports: WITH cte1 AS (...), cte2 AS (...), ...
        var pattern = @"\bWITH\s+(?:RECURSIVE\s+)?([a-zA-Z_][a-zA-Z0-9_]*|""[^""]+"")\s+AS\s*\(";
        
        var match = Regex.Match(sql, pattern, RegexOptions.IgnoreCase);
        if (match.Success)
        {
            // Extract first CTE name
            cteNames.Add(match.Groups[1].Value.Trim('"'));
            
            // Look for additional CTEs: , cte_name AS (
            var additionalPattern = @",\s*([a-zA-Z_][a-zA-Z0-9_]*|""[^""]+"")\s+AS\s*\(";
            foreach (Match additionalMatch in Regex.Matches(sql, additionalPattern, RegexOptions.IgnoreCase))
            {
                cteNames.Add(additionalMatch.Groups[1].Value.Trim('"'));
            }
        }
        
        return cteNames;
    }

    /// <summary>
    /// Extracts CTE aliases from FROM/JOIN clauses where CTEs are used.
    /// Example: "FROM current_month cm" → "cm" is an alias for CTE "current_month"
    /// These aliases should be excluded from column validation (CTEs define their own columns).
    /// </summary>
    private HashSet<string> ExtractCteAliases(string sql, HashSet<string> cteNames)
    {
        var cteAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        // Add the CTE names themselves (they can be used without aliases)
        foreach (var cteName in cteNames)
        {
            cteAliases.Add(cteName);
        }
        
    // Pattern: FROM/JOIN/,(,) cte_name [AS] alias
    // Example: FROM current_month cm, current_month AS cm
    var pattern = @"(?:\b(?:FROM|JOIN)\s+|,)" +
             @"\s*([a-zA-Z_][a-zA-Z0-9_]*|""[^""]+"")" +             // CTE name
             @"(?:\s+(?:AS\s+)?([a-zA-Z_][a-zA-Z0-9_]*|""[^""]+""))?";  // optional alias
        
        var sqlWithoutFunctions = RemoveFunctionContents(sql);
        
        foreach (Match match in Regex.Matches(sqlWithoutFunctions, pattern, RegexOptions.IgnoreCase))
        {
            var tableName = match.Groups[1].Value.Trim('"');
            var aliasName = match.Groups[2].Value.Trim('"');
            
            // Only process if this is a CTE reference
            if (cteNames.Contains(tableName))
            {
                // Add the alias (if present) or the CTE name itself
                if (!string.IsNullOrEmpty(aliasName))
                {
                    cteAliases.Add(aliasName);
                }
                else
                {
                    cteAliases.Add(tableName);
                }
            }
        }
        
        return cteAliases;
    }

    /// <summary>
    /// Validates a SQL query for safety with defense-in-depth protection.
    /// Returns (isValid, errorMessage).
    /// </summary>
    public (bool isValid, string? errorMessage) ValidateSql(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return (false, "SQL query is empty");

        // 1. HARD BLOCK: No semicolons allowed (prevents SQL injection via statement stacking)
        // The DB driver should also be configured to disallow multiple statements
        if (sql.Contains(';'))
        {
            _logger.LogWarning("Query rejected: semicolons not allowed");
            return (false, "Semicolons are not allowed. Only single statements permitted.");
        }

        // 2. Strip comments and string literals to avoid false positives in keyword scanning
        var scan = StripForScan(sql).Trim();
        var normalized = scan.ToUpperInvariant();

        // 3. Must start with SELECT or WITH (for CTEs - Common Table Expressions)
        if (!normalized.StartsWith("SELECT") && !normalized.StartsWith("WITH"))
        {
            _logger.LogWarning("[VAL001_NOT_SELECT] Query rejected: must start with SELECT or WITH. Query starts with: {Start}", 
                normalized.Length > 30 ? normalized.Substring(0, 30) + "..." : normalized);
            return (false, "Only SELECT queries (including CTEs with WITH clause) are allowed");
        }

        // 3.5. If WITH is used, enforce that it culminates in SELECT (not DML/DDL)
        if (normalized.StartsWith("WITH"))
        {
            // After WITH-CTEs, the outer statement must be SELECT
            // Regex checks if SELECT keyword exists after the CTE definitions
            if (!Regex.IsMatch(scan, @"\)\s*SELECT\b", RegexOptions.IgnoreCase))
            {
                _logger.LogWarning("[VAL002_WITH_NO_SELECT] Query rejected: WITH clause must culminate in SELECT");
                return (false, "WITH queries must end with a SELECT statement, not DML/DDL operations.");
            }
        }

        // 4. CRITICAL: Block SELECT ... INTO (creates new tables)
        // Supports quoted and schema-qualified targets: INTO "table", INTO schema.table, etc.
    if (Regex.IsMatch(scan, @"\bSELECT\b[\s\S]*?\bINTO\b\s+(?!(OUTFILE|DUMPFILE)\b)(?:""[^""]+""|\w+)(?:\.(?:""[^""]+""|\w+))?", RegexOptions.IgnoreCase))
        {
            _logger.LogWarning("[VAL003_SELECT_INTO] Query rejected: SELECT INTO detected (table creation)");
            return (false, "SELECT ... INTO is not allowed (creates tables)");
        }

        // 4.1 Block SELECT ... INTO OUTFILE/DUMPFILE (file export)
        if (Regex.IsMatch(scan, @"\bINTO\s+(OUTFILE|DUMPFILE)\b", RegexOptions.IgnoreCase))
        {
            _logger.LogWarning("[VAL003_SELECT_INTO_OUTFILE] Query rejected: SELECT INTO OUTFILE detected");
            return (false, "SELECT ... INTO OUTFILE/DUMPFILE is not allowed (creates exports)");
        }

        // 5. Check for dangerous patterns (DML, DDL, DCL, execution, exports, etc.)
        foreach (var pattern in DangerousPatterns)
        {
            if (Regex.IsMatch(scan, pattern, RegexOptions.IgnoreCase))
            {
                var keyword = pattern.Replace(@"\b", "").Replace(@"(?:UTE)?", "UTE")
                    .Replace(@"(?!\s+(?:WORK|TRANSACTION))?", "")
                    .Replace(@"\s+", " ");
                _logger.LogWarning("[VAL004_DANGEROUS_OP] Dangerous pattern detected: {Pattern}", keyword);
                return (false, $"Dangerous operation not allowed: {keyword}. Only read-only SELECT queries are permitted.");
            }
        }

        // 5.25. Block common boolean tautologies used in SQL injection (e.g., OR 1=1, OR ''='')
        foreach (var pattern in SqlInjectionPatterns)
        {
            if (pattern.IsMatch(scan))
            {
                _logger.LogWarning("[VAL005_INJECTION_PATTERN] Query rejected: boolean tautology detected");
                return (false, "Potential SQL injection pattern detected (boolean tautology)");
            }
        }

        // 5.5. CRITICAL: Block derived tables in FROM/JOIN to prevent table allowlist bypass
        // Pattern: FROM ( ... ) or JOIN ( ... )
        // Derived tables can hide base tables from our allowlist check.
        // Policy: require CTEs (WITH clause) instead of inline subqueries.
        if (Regex.IsMatch(scan, @"\b(?:FROM|JOIN)\s*\(", RegexOptions.IgnoreCase))
        {
            _logger.LogWarning("[VAL005_DERIVED_TABLE] Query rejected: derived table detected (FROM/JOIN subquery)");
            return (false, "Derived tables (FROM (subquery)) are not allowed. Use CTEs (WITH clause) instead.");
        }

        // 6. Block SELECT * (requires explicit column selection for security)
        if (ContainsSelectWildcard(scan))
        {
            _logger.LogWarning("[VAL006_SELECT_STAR] Query rejected: SELECT * not allowed");
            return (false, "SELECT * is not allowed. Please specify explicit columns.");
        }

        // 7. Extract CTE names to exclude from table allowlist checks
        var cteNames = ExtractCteNames(scan);

        // 8. Extract and validate table names with alias resolution (excluding CTEs)
        var aliasMap = ExtractTableAliases(scan, cteNames);
        
        // 8.5. Extract CTE aliases (for column validation exclusion)
        // Example: "FROM current_month cm" → "cm" maps to CTE "current_month"
        var cteAliases = ExtractCteAliases(scan, cteNames);
        
        var tables = ExtractTableNames(scan, aliasMap, cteNames);
        
        foreach (var table in tables)
        {
            if (!_allowlist.IsTableAllowed(table))
            {
                _logger.LogWarning("[VAL007_TABLE_NOT_ALLOWED] Unauthorized table access attempt: {Table}", table);
                return (false, $"Table not allowed: {table}");
            }
        }

        // 9. Validate column references with alias resolution (excluding CTE aliases)
        var result = ValidateColumnsWithAliases(scan, aliasMap, cteAliases);
        if (!result.isValid)
            return result;

        // 10. CRITICAL: Validate unqualified (bare) columns in SELECT list
        // Policy: Only allow if exactly one base table and no derived tables.
        // This prevents bypassing column allowlist via unqualified column names.
        result = ValidateBareColumns(scan, tables);
        if (!result.isValid)
            return result;

        _logger.LogInformation("SQL validation passed for query");
        return (true, null);
    }

    /// <summary>
    /// Extracts table aliases from FROM and JOIN clauses.
    /// Handles schema-qualified names, quoted identifiers, ONLY keyword, and excludes CTEs.
    /// CRITICAL: Must not match FROM inside function calls like EXTRACT(MONTH FROM date_column).
    /// Returns a map of alias -> (schema, table_name).
    /// </summary>
    private Dictionary<string, (string schema, string table)> ExtractTableAliases(
        string sql, 
        HashSet<string> cteNames)
    {
        var map = new Dictionary<string, (string schema, string table)>(StringComparer.OrdinalIgnoreCase);
        
        // Remove function call contents first to avoid false positives
        var sqlWithoutFunctions = RemoveFunctionContents(sql);
        
    // Pattern: FROM/JOIN [ONLY] [schema.]table [AS] alias
    // Supports: public.users u, ONLY "public"."users" AS u, ONLY users u, etc.
    var pattern = @"\b(?:FROM|JOIN)\s+" +
             @"(?:ONLY\s+)?" +                                       // optional ONLY keyword
             @"(?:([a-zA-Z_][a-zA-Z0-9_]*|""[^""]+"")\.)?" +         // optional schema
             @"([a-zA-Z_][a-zA-Z0-9_]*|""[^""]+"")" +                // table name
             @"(?:\s+(?:AS\s+)?([a-zA-Z_][a-zA-Z0-9_]*|""[^""]+""))?";  // optional alias
        
        foreach (Match match in Regex.Matches(sqlWithoutFunctions, pattern, RegexOptions.IgnoreCase))
        {
            var schemaGroup = match.Groups[1].Value;
            var tableGroup = match.Groups[2].Value;
            var aliasGroup = match.Groups[3].Value;
            
            var table = tableGroup.Trim('"');
            
            // Skip CTEs - they are not real tables
            if (cteNames.Contains(table))
                continue;
            
            var schema = !string.IsNullOrEmpty(schemaGroup) 
                ? schemaGroup.Trim('"') 
                : "public";
            var alias = !string.IsNullOrEmpty(aliasGroup) 
                ? aliasGroup.Trim('"') 
                : table;
            
            map[alias] = (schema, table);
        }
        
        return map;
    }

    /// <summary>
    /// Extracts table names from the query, resolving aliases and excluding CTEs.
    /// </summary>
    private HashSet<string> ExtractTableNames(
        string sql, 
        Dictionary<string, (string schema, string table)> aliasMap,
        HashSet<string> cteNames)
    {
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        // Add all actual table names (not aliases, not CTEs) from the alias map
        foreach (var kvp in aliasMap)
        {
            // Skip if it's a CTE
            if (!cteNames.Contains(kvp.Value.table))
            {
                tables.Add(kvp.Value.table);
            }
        }
        
        // Also try regex-based extraction as fallback
        try
        {
            var regexTables = ExtractTableNamesRegex(sql);
            foreach (var table in regexTables)
            {
                // Skip CTEs
                if (!cteNames.Contains(table))
                {
                    tables.Add(table);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract tables via regex fallback");
        }
        
        return tables;
    }

    /// <summary>
    /// Validates column references with alias resolution.
    /// Checks patterns like: alias.column, schema.table.column, table.column
    /// CRITICAL: Skips CTE aliases since CTEs are virtual tables defined in the query itself.
    /// </summary>
    private (bool isValid, string? errorMessage) ValidateColumnsWithAliases(
        string sql, 
        Dictionary<string, (string schema, string table)> aliasMap,
        HashSet<string> cteAliases)
    {
        // Pattern: [schema.]table.column or alias.column
        // Supports quoted identifiers: "schema"."table"."column"
        var pattern = @"(?:([a-zA-Z_][a-zA-Z0-9_]*|""[^""]+"")\.)?" +  // optional schema/table/alias
                     @"([a-zA-Z_][a-zA-Z0-9_]*|""[^""]+"")\." +         // table/alias
                     @"([a-zA-Z_][a-zA-Z0-9_]*|""[^""]+"")";            // column
        
        foreach (Match match in Regex.Matches(sql, pattern, RegexOptions.IgnoreCase))
        {
            var part1 = match.Groups[1].Value.Trim('"');  // schema or table or alias
            var part2 = match.Groups[2].Value.Trim('"');  // table or alias
            var column = match.Groups[3].Value.Trim('"'); // column
            
            // CRITICAL: Skip if this is a CTE alias (CTEs are query-defined, not real tables)
            // Example: "cm.total_revenue" where "cm" is alias for CTE "current_month"
            if (cteAliases.Contains(part2))
            {
                // This is a CTE reference, skip validation (CTEs define their own columns)
                continue;
            }
            
            string? table = null;
            
            if (!string.IsNullOrEmpty(part1))
            {
                // Three parts: schema.table.column or alias.something.column
                // Try to resolve part2 as alias first, then as table
                if (aliasMap.TryGetValue(part2, out var resolved))
                {
                    table = resolved.table;
                }
                else
                {
                    table = part2;
                }
            }
            else
            {
                // Two parts: table.column or alias.column
                if (aliasMap.TryGetValue(part2, out var resolved))
                {
                    table = resolved.table;
                }
                else
                {
                    table = part2;
                }
            }
            
            if (table != null && !_allowlist.IsColumnAllowed(table, column))
            {
                _logger.LogWarning("[VAL008_COLUMN_NOT_ALLOWED] Unauthorized column access: {Table}.{Column}", table, column);
                return (false, $"Column not allowed: {table}.{column}");
            }
        }
        
        return (true, null);
    }

    /// <summary>
    /// CRITICAL: Validates unqualified (bare) columns in SELECT list.
    /// Policy: Only allowed if exactly one base table (and no derived tables already blocked).
    /// This prevents bypassing the column allowlist via "SELECT amount FROM expenses" (no "e." prefix).
    /// 
    /// IMPORTANT: Filters out aliases (AS name), function names (func()), and type casts (::type).
    /// </summary>
    private (bool isValid, string? errorMessage) ValidateBareColumns(
        string sql,
        HashSet<string> baseTables)
    {
        var selectList = ExtractTopLevelSelectList(sql);

        if (selectList is null)
            return (true, null); // No FROM clause in outer query
        
        // Find unqualified identifiers (not followed by a dot)
        // Pattern: word boundary + identifier + NOT followed by '.' or '('
    var bareIdentifiers = Regex.Matches(selectList, @"(?<!\.)\b([a-zA-Z_][a-zA-Z0-9_]*)\b(?!\s*[\.\(])");
        
        if (bareIdentifiers.Count == 0)
            return (true, null); // All columns are qualified, perfect!
        
        // Filter out SQL keywords and function names (common false positives)
        var sqlKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AS", "CASE", "END", "WHEN", "THEN", "ELSE", "NULL", "TRUE", "FALSE",
            "DISTINCT", "ON", "OVER", "PARTITION", "FILTER", "ORDER", "BY",
            "AND", "OR", "NOT", "IN", "IS", "BETWEEN", "LIKE", "ILIKE",
            "CAST", "EXTRACT", "SUBSTRING", "COALESCE", "NULLIF",
            "COUNT", "SUM", "AVG", "MIN", "MAX", "ARRAY_AGG", "STRING_AGG",
            "ROW_NUMBER", "RANK", "DENSE_RANK", "LAG", "LEAD",
            "LIMIT", "OFFSET", "FETCH", "NEXT", "FIRST", "LAST",
            // PostgreSQL type names (for ::type casts)
            "INTEGER", "INT", "BIGINT", "SMALLINT", "NUMERIC", "DECIMAL", "REAL", "DOUBLE",
            "VARCHAR", "CHAR", "TEXT", "BOOLEAN", "BOOL", "DATE", "TIME", "TIMESTAMP",
            "TIMESTAMPTZ", "INTERVAL", "JSON", "JSONB", "UUID", "BYTEA", "ARRAY"
        };
        
        var actualColumns = new List<string>();
        foreach (Match match in bareIdentifiers)
        {
            var identifier = match.Groups[1].Value;
            
            // Skip SQL keywords
            if (sqlKeywords.Contains(identifier))
                continue;
            
            // Skip if it's an alias (appears after AS keyword)
            // Pattern: AS identifier
            if (Regex.IsMatch(selectList, $@"\bAS\s+{Regex.Escape(identifier)}\b", 
                RegexOptions.IgnoreCase))
                continue;
            
            // Skip if it's a type cast target (appears after ::)
            // Pattern: ::identifier (e.g., amount::numeric)
            if (Regex.IsMatch(selectList, $@"::\s*{Regex.Escape(identifier)}\b", 
                RegexOptions.IgnoreCase))
                continue;
            
            actualColumns.Add(identifier);
        }
        
        if (actualColumns.Count == 0)
            return (true, null); // Only keywords/aliases/casts found, no actual columns
        
        // Policy enforcement: Unqualified columns only allowed with exactly one base table
        if (baseTables.Count != 1)
        {
            _logger.LogWarning("[VAL009_BARE_COL_MULTI_TABLE] Unqualified columns detected with {Count} tables: {Columns}", 
                baseTables.Count, string.Join(", ", actualColumns));
            return (false, 
                "Unqualified columns are not allowed with multiple tables. Use table/alias.column syntax.");
        }
        
        // Validate each bare column against the single table
        var singleTable = baseTables.First();
        foreach (var column in actualColumns)
        {
            if (!_allowlist.IsColumnAllowed(singleTable, column))
            {
                _logger.LogWarning("[VAL010_BARE_COL_NOT_ALLOWED] Unauthorized bare column access: {Column} (inferred from {Table})", 
                    column, singleTable);
                return (false, $"Column not allowed: {singleTable}.{column}");
            }
        }
        
        _logger.LogInformation("Bare columns validated against single table: {Table}", singleTable);
        return (true, null);
    }

    private static string? ExtractTopLevelSelectList(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return null;

        const string selectKeyword = "SELECT";
        const string fromKeyword = "FROM";

        var length = sql.Length;
        var selectStart = -1;
        var inLineComment = false;
        var inBlockComment = false;
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var parenDepth = 0;

        for (var i = 0; i < length; i++)
        {
            var c = sql[i];

            if (inLineComment)
            {
                if (c == '\n' || c == '\r')
                    inLineComment = false;
                continue;
            }

            if (inBlockComment)
            {
                if (c == '*' && i + 1 < length && sql[i + 1] == '/')
                {
                    inBlockComment = false;
                    i++; // skip closing '/'
                }
                continue;
            }

            if (inSingleQuote)
            {
                if (c == '\'')
                {
                    if (i + 1 < length && sql[i + 1] == '\'')
                    {
                        i++; // escaped quote ''
                    }
                    else
                    {
                        inSingleQuote = false;
                    }
                }
                continue;
            }

            if (inDoubleQuote)
            {
                if (c == '"')
                {
                    if (i + 1 < length && sql[i + 1] == '"')
                    {
                        i++;
                    }
                    else
                    {
                        inDoubleQuote = false;
                    }
                }
                continue;
            }

            if (c == '-' && i + 1 < length && sql[i + 1] == '-')
            {
                inLineComment = true;
                i++;
                continue;
            }

            if (c == '/' && i + 1 < length && sql[i + 1] == '*')
            {
                inBlockComment = true;
                i++;
                continue;
            }

            if (c == '\'')
            {
                inSingleQuote = true;
                continue;
            }

            if (c == '"')
            {
                inDoubleQuote = true;
                continue;
            }

            if (c == '(')
            {
                parenDepth++;
                continue;
            }

            if (c == ')' && parenDepth > 0)
            {
                parenDepth--;
                continue;
            }

            if (parenDepth == 0)
            {
                if (selectStart == -1 && IsKeywordAt(sql, i, selectKeyword))
                {
                    selectStart = i + selectKeyword.Length;
                    i += selectKeyword.Length - 1;
                    continue;
                }

                if (selectStart != -1 && IsKeywordAt(sql, i, fromKeyword))
                {
                    return sql.Substring(selectStart, i - selectStart);
                }
            }
        }

        return null;
    }

    private static bool IsKeywordAt(string sql, int index, string keyword)
    {
        var length = keyword.Length;
        if (index < 0 || index + length > sql.Length)
            return false;

        if (!sql.AsSpan(index, length).Equals(keyword, StringComparison.OrdinalIgnoreCase))
            return false;

        var beforeOk = index == 0 || (!char.IsLetterOrDigit(sql[index - 1]) && sql[index - 1] != '_');
        var afterIndex = index + length;
        var afterOk = afterIndex >= sql.Length || (!char.IsLetterOrDigit(sql[afterIndex]) && sql[afterIndex] != '_');

        return beforeOk && afterOk;
    }

    /// <summary>
    /// Fallback regex-based table extraction.
    /// Extracts tables from FROM and JOIN clauses, handling schema-qualified names and ONLY keyword.
    /// CRITICAL: Must not match FROM inside function calls like EXTRACT(MONTH FROM date_column).
    /// </summary>
    private HashSet<string> ExtractTableNamesRegex(string sql)
    {
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        // Strategy: Remove function calls first to avoid false positives
        // Pattern to match function calls: func_name(...)
        // We'll replace function contents with spaces to avoid matching FROM inside EXTRACT(), etc.
        var sqlWithoutFunctions = RemoveFunctionContents(sql);
        
        // Match FROM/JOIN [ONLY] [schema.]table_name
        // Handles: FROM users, FROM ONLY public.users, FROM "public"."users", etc.
        var pattern = @"\b(?:FROM|JOIN)\s+" +
                     @"(?:ONLY\s+)?" +                                               // optional ONLY
                     @"(?:[a-zA-Z_][a-zA-Z0-9_]*\.|""[^""]+""\.)?" +                // optional schema
                     @"([a-zA-Z_][a-zA-Z0-9_]*|""[^""]+"")" +                       // table name
                     @"(?:\s+(?:AS\s+)?(?:[a-zA-Z_][a-zA-Z0-9_]*|""[^""]+""))?";   // optional alias
        
        foreach (Match match in Regex.Matches(sqlWithoutFunctions, pattern, RegexOptions.IgnoreCase))
        {
            if (match.Groups.Count > 1)
            {
                var tableName = match.Groups[1].Value.Trim('"');
                tables.Add(tableName);
            }
        }

        return tables;
    }

    /// <summary>
    /// Removes function call contents to avoid parsing function arguments as table references.
    /// Example: "EXTRACT(MONTH FROM orderdate)" -> "EXTRACT(            )"
    /// This prevents "orderdate" from being matched as a table name after "FROM".
    /// </summary>
    private string RemoveFunctionContents(string sql)
    {
        if (string.IsNullOrEmpty(sql))
            return sql;

        var chars = sql.ToCharArray();
        var stack = new Stack<(int index, bool isFunction)>();

        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (c == '(')
            {
                var j = i - 1;
                while (j >= 0 && char.IsWhiteSpace(chars[j]))
                {
                    j--;
                }

                var isFunction = j >= 0 && (char.IsLetterOrDigit(chars[j]) || chars[j] == '_' || chars[j] == '.' || chars[j] == ')');
                stack.Push((i, isFunction));
            }
            else if (c == ')' && stack.Count > 0)
            {
                var (start, isFunction) = stack.Pop();
                if (!isFunction || i - start <= 1)
                    continue;

                // Inspect inner text to avoid blanking subqueries (which start with SELECT/WITH)
                var innerSpan = new ReadOnlySpan<char>(chars, start + 1, i - start - 1);
                var trimmed = innerSpan.Trim();
                if (trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                for (var k = start + 1; k < i; k++)
                {
                    if (!char.IsWhiteSpace(chars[k]))
                    {
                        chars[k] = ' ';
                    }
                }
            }
        }

        return new string(chars);
    }

    private static bool ContainsSelectWildcard(string sql)
    {
        var selectList = ExtractTopLevelSelectList(sql);
        if (string.IsNullOrWhiteSpace(selectList))
            return false;

        if (Regex.IsMatch(selectList, @"(^|[\s,])\*(?=([\s,]|$))", RegexOptions.IgnoreCase))
            return true;

        if (Regex.IsMatch(selectList, @"\b[a-zA-Z_][a-zA-Z0-9_]*\.\*", RegexOptions.IgnoreCase))
            return true;

        return false;
    }

    /// <summary>
    /// Checks if the query has a reasonable LIMIT clause to prevent large result sets.
    /// PostgreSQL-specific implementation.
    /// IMPORTANT: Only checks the outermost LIMIT (near end of query), not subquery LIMITs.
    /// </summary>
    public bool HasReasonableLimit(string sql)
    {
        // Strip comments and strings before checking for LIMIT
        var scan = StripForScan(sql).TrimEnd();
        
        // Pattern: LIMIT N at the end, optionally followed by OFFSET N
        // This ensures we're checking the outermost query's LIMIT, not a subquery's
        var limitMatch = Regex.Match(scan, @"\bLIMIT\s+(\d+)(?:\s+OFFSET\s+\d+)?\s*$", 
            RegexOptions.IgnoreCase);
        
        if (limitMatch.Success && int.TryParse(limitMatch.Groups[1].Value, out var limit))
        {
            return limit <= _allowlist.MaxLimit;
        }
        
        // No LIMIT clause found at the end
        return false;
    }

    /// <summary>
    /// Adds or clamps LIMIT clause for PostgreSQL queries.
    /// Ensures queries don't return excessive rows.
    /// </summary>
    public string EnsureLimit(string sql, int maxLimit)
    {
        var scan = StripForScan(sql);
        var effectiveMax = Math.Min(maxLimit, _allowlist.MaxLimit);
        
        if (!HasReasonableLimit(sql))
        {
            // Remove any existing LIMIT that might be buried in subqueries (outermost only)
            sql = Regex.Replace(sql, @"\bLIMIT\s+\d+\s*$", "", RegexOptions.IgnoreCase).TrimEnd();
            
            // Add reasonable LIMIT at the end
            return $"{sql} LIMIT {effectiveMax}";
        }
        
        // Clamp existing LIMIT if it exceeds max
        return Regex.Replace(sql, @"\bLIMIT\s+(\d+)", match =>
        {
            if (int.TryParse(match.Groups[1].Value, out var limit))
            {
                return $"LIMIT {Math.Min(limit, effectiveMax)}";
            }
            return match.Value;
        }, RegexOptions.IgnoreCase);
    }
}
