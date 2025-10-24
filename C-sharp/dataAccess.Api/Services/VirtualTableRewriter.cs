using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace dataAccess.Api.Services;

/// <summary>
/// Rewrites SQL queries to expand virtual table references into actual SQL joins.
/// Handles business concepts like "sales" that don't exist as physical tables but are derived from joins.
/// </summary>
public class VirtualTableRewriter
{
    private readonly ILogger<VirtualTableRewriter> _logger;

    // Virtual table definitions: maps virtual table name to its SQL source (FROM clause with joins)
    private static readonly Dictionary<string, VirtualTableDefinition> VirtualTables = 
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Note: 'sales' is actually a database VIEW, so it doesn't need rewriting.
            // But if you create other virtual concepts that don't exist in DB, define them here.
            
            // Example: If you want a virtual "inventory" table
            // ["inventory"] = new VirtualTableDefinition
            // {
            //     Alias = "inv",
            //     SqlSource = @"products p 
            //         JOIN productcategory pc ON p.productid = pc.productid",
            //     AvailableColumns = new[] { "p.productid", "p.productname", "pc.currentstock", "pc.reorderpoint", "pc.price", "pc.cost" }
            // }
        };

    public VirtualTableRewriter(ILogger<VirtualTableRewriter> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Checks if a SQL query references any virtual tables and rewrites them if needed.
    /// Returns the rewritten SQL or the original if no virtual tables are found.
    /// </summary>
    public string RewriteIfNeeded(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return sql;

        var rewritten = sql;
        var hasRewrite = false;

        foreach (var kvp in VirtualTables)
        {
            var virtualTable = kvp.Key;
            var definition = kvp.Value;

            // Check if SQL references this virtual table in FROM or JOIN
            // Pattern: FROM virtual_table or JOIN virtual_table
            var pattern = $@"\b(FROM|JOIN)\s+{Regex.Escape(virtualTable)}\b";
            
            if (Regex.IsMatch(rewritten, pattern, RegexOptions.IgnoreCase))
            {
                _logger.LogInformation("Rewriting virtual table: {VirtualTable}", virtualTable);
                
                // Replace FROM virtual_table with FROM (actual joins) AS alias
                // This is a simplified rewrite; for production, you may need a SQL parser
                var replacement = $"$1 ({definition.SqlSource}) AS {definition.Alias}";
                rewritten = Regex.Replace(rewritten, pattern, replacement, RegexOptions.IgnoreCase);
                
                hasRewrite = true;
            }
        }

        if (hasRewrite)
        {
            _logger.LogInformation("Rewritten SQL: {RewrittenSql}", rewritten);
        }

        return rewritten;
    }

    /// <summary>
    /// Checks if a table name is a virtual table.
    /// </summary>
    public bool IsVirtualTable(string tableName)
    {
        return VirtualTables.ContainsKey(tableName);
    }

    /// <summary>
    /// Gets the list of virtual table names.
    /// </summary>
    public IReadOnlyCollection<string> GetVirtualTableNames()
    {
        return VirtualTables.Keys;
    }
}

/// <summary>
/// Defines a virtual table with its SQL source and available columns.
/// </summary>
public class VirtualTableDefinition
{
    /// <summary>
    /// Alias to use for the rewritten subquery.
    /// </summary>
    public string Alias { get; set; } = string.Empty;

    /// <summary>
    /// The SQL source (FROM clause with joins) that defines the virtual table.
    /// </summary>
    public string SqlSource { get; set; } = string.Empty;

    /// <summary>
    /// Optional: List of available columns for this virtual table.
    /// </summary>
    public string[] AvailableColumns { get; set; } = Array.Empty<string>();
}
