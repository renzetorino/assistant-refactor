using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace dataAccess.Api.Services;

public interface IDatabaseSchemaService
{
	Task<string> GetRelevantSchemaAsync(string userQuery);
	Task<string> GetTableSchemaAsync(string tableName);
	SchemaMappingDiagnostics GetDiagnostics(bool reload = false);
}

public sealed record SchemaCacheSummary(string Table, DateTimeOffset CachedAt, DateTimeOffset ExpiresAt, bool IsExpired);

public sealed record SchemaMappingDiagnostics(
	IReadOnlyCollection<string> AvailableTables,
	IReadOnlyDictionary<string, IReadOnlyCollection<string>> KeywordMappings,
	IReadOnlyCollection<SchemaCacheSummary> CacheEntries,
	IReadOnlyCollection<string> DefaultFallbackTables);

public class DatabaseSchemaService : IDatabaseSchemaService
{
	private readonly IConfiguration _configuration;
	private readonly ILogger<DatabaseSchemaService> _logger;
	private readonly ConcurrentDictionary<string, SchemaCacheEntry> _schemaCache;
	private readonly TimeSpan _schemaCacheTtl;
	private readonly object _syncRoot = new();

	private HashSet<string> _availableTables = new(StringComparer.OrdinalIgnoreCase);
	private Dictionary<string, string[]> _keywordToTables = new(StringComparer.OrdinalIgnoreCase);
	private string[] _defaultFallbackTables = Array.Empty<string>();

	private sealed record SchemaCacheEntry(string Schema, DateTimeOffset CachedAt);

	public DatabaseSchemaService(
		IConfiguration configuration,
		ILogger<DatabaseSchemaService> logger)
	{
		_configuration = configuration;
		_logger = logger;
		_schemaCache = new ConcurrentDictionary<string, SchemaCacheEntry>(StringComparer.OrdinalIgnoreCase);

		var ttlSeconds = Math.Max(configuration.GetValue<int?>("DatabaseSchema:CacheTtlSeconds") ?? 3600, 60);
		_schemaCacheTtl = TimeSpan.FromSeconds(ttlSeconds);

		ReloadMappings();
	}

	public SchemaMappingDiagnostics GetDiagnostics(bool reload = false)
	{
		if (reload)
		{
			ReloadMappings();
		}

		Dictionary<string, string[]> keywordSnapshot;
		HashSet<string> availableSnapshot;
		string[] fallbackSnapshot;

		lock (_syncRoot)
		{
			keywordSnapshot = _keywordToTables;
			availableSnapshot = _availableTables;
			fallbackSnapshot = _defaultFallbackTables;
		}

		var keywordMappings = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase);
		foreach (var entry in keywordSnapshot.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
		{
			keywordMappings[entry.Key] = entry.Value;
		}

		var now = DateTimeOffset.UtcNow;
		var cacheEntries = _schemaCache
			.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
			.Select(kvp =>
			{
				var expiresAt = kvp.Value.CachedAt + _schemaCacheTtl;
				var isExpired = expiresAt <= now;
				return new SchemaCacheSummary(kvp.Key, kvp.Value.CachedAt, expiresAt, isExpired);
			})
			.ToArray();

		var availableTables = availableSnapshot
			.OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
			.ToArray();

		var defaultFallback = fallbackSnapshot
			.OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
			.ToArray();

		return new SchemaMappingDiagnostics(availableTables, keywordMappings, cacheEntries, defaultFallback);
	}

	public async Task<string> GetRelevantSchemaAsync(string userQuery)
	{
		var keywords = ExtractKeywords(userQuery ?? string.Empty);
		var relevantTables = GetRelevantTables(keywords);

		if (relevantTables.Count == 0)
		{
			string[] fallbackSnapshot;
			lock (_syncRoot)
			{
				fallbackSnapshot = _defaultFallbackTables;
			}

			relevantTables = new HashSet<string>(fallbackSnapshot, StringComparer.OrdinalIgnoreCase);
		}

		if (relevantTables.Count == 0)
		{
			_logger.LogWarning("No relevant tables found for query: {Query}", userQuery);
			return string.Empty;
		}

		var schemas = new List<string>(relevantTables.Count);
		foreach (var tableName in relevantTables.OrderBy(t => t, StringComparer.OrdinalIgnoreCase))
		{
			var schema = await GetTableSchemaAsync(tableName);
			if (!string.IsNullOrWhiteSpace(schema))
			{
				schemas.Add(schema);
			}
		}

		var combined = string.Join("\n\n", schemas);
		_logger.LogInformation(
			"Retrieved schema for {Count} table(s) based on keywords: {Keywords}",
			schemas.Count,
			string.Join(", ", keywords));

		return combined;
	}

	public async Task<string> GetTableSchemaAsync(string tableName)
	{
		if (string.IsNullOrWhiteSpace(tableName))
		{
			return string.Empty;
		}

		var normalizedTable = tableName.Trim().ToLowerInvariant();
		var available = GetAvailableTablesSnapshot();
		if (!available.Contains(normalizedTable))
		{
			ReloadMappings();
			available = GetAvailableTablesSnapshot();

			if (!available.Contains(normalizedTable))
			{
				_logger.LogWarning("Requested schema for unknown table {TableName}", normalizedTable);
				return string.Empty;
			}
		}

		if (_schemaCache.TryGetValue(normalizedTable, out var cachedEntry))
		{
			if (cachedEntry.CachedAt + _schemaCacheTtl > DateTimeOffset.UtcNow)
			{
				return cachedEntry.Schema;
			}

			_schemaCache.TryRemove(normalizedTable, out _);
		}

		try
		{
			var connectionString = _configuration.GetConnectionString("DefaultConnection");
			if (string.IsNullOrWhiteSpace(connectionString))
			{
				_logger.LogWarning("DefaultConnection is not configured; cannot fetch schema for table {TableName}", normalizedTable);
				return string.Empty;
			}

			await using var connection = new NpgsqlConnection(connectionString);
			await connection.OpenAsync();

			const string query = """
				SELECT 
					c.column_name,
					c.data_type,
					c.is_nullable,
					c.column_default,
					tc.constraint_type
				FROM information_schema.columns c
				LEFT JOIN information_schema.key_column_usage kcu 
					ON c.table_name = kcu.table_name 
					AND c.column_name = kcu.column_name
					AND c.table_schema = kcu.table_schema
				LEFT JOIN information_schema.table_constraints tc 
					ON kcu.constraint_name = tc.constraint_name
					AND kcu.table_schema = tc.table_schema
				WHERE c.table_schema = 'public' 
					AND c.table_name = @tableName
				ORDER BY c.ordinal_position;
			""";

			await using var cmd = new NpgsqlCommand(query, connection);
			cmd.Parameters.AddWithValue("@tableName", normalizedTable);

			await using var reader = await cmd.ExecuteReaderAsync();

			if (!reader.HasRows)
			{
				_logger.LogWarning("Table {TableName} not found in schema", normalizedTable);
				return string.Empty;
			}

			var ddl = new StringBuilder();
			ddl.AppendLine($"CREATE TABLE public.{normalizedTable} (");

			var columns = new List<string>();
			while (await reader.ReadAsync())
			{
				var columnName = reader.GetString(0);
				var dataType = reader.GetString(1);
				var isNullable = reader.GetString(2) == "YES" ? string.Empty : " NOT NULL";
				var columnDefault = reader.IsDBNull(3) ? string.Empty : $" DEFAULT {reader.GetString(3)}";
				var constraintType = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);

				var columnDef = $"  {columnName} {dataType.ToUpperInvariant()}{isNullable}{columnDefault}";

				if (constraintType == "PRIMARY KEY")
				{
					columnDef += " PRIMARY KEY";
				}

				columns.Add(columnDef);
			}

			ddl.AppendLine(string.Join(",\n", columns));
			ddl.Append(");");

			var schema = ddl.ToString();
			_schemaCache[normalizedTable] = new SchemaCacheEntry(schema, DateTimeOffset.UtcNow);

			_logger.LogDebug("Retrieved schema for table: {TableName}", normalizedTable);
			return schema;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error fetching schema for table: {TableName}", normalizedTable);
			return string.Empty;
		}
	}

	private void ReloadMappings()
	{
		var tables = LoadAvailableTables();
		if (tables.Count == 0)
		{
			_logger.LogWarning("No tables discovered when loading schema metadata; keeping existing mappings.");
			return;
		}

		var keywordMap = BuildKeywordMappings(tables);
		var fallback = BuildDefaultFallbackTables(tables);

		lock (_syncRoot)
		{
			_availableTables = tables;
			_keywordToTables = keywordMap;
			_defaultFallbackTables = fallback;
		}

		_logger.LogInformation("Loaded {Count} tables for schema mapping.", tables.Count);
	}

	private HashSet<string> GetAvailableTablesSnapshot()
	{
		lock (_syncRoot)
		{
			return _availableTables;
		}
	}

	private static Dictionary<string, string[]> BuildKeywordMappings(ISet<string> availableTables)
	{
		var map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

		void Register(IEnumerable<string> keywords, params string[] candidateTables)
		{
			var filtered = candidateTables
				.Select(t => t.ToLowerInvariant())
				.Where(availableTables.Contains)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToArray();

			if (filtered.Length == 0)
			{
				return;
			}

			foreach (var keyword in keywords)
			{
				map[keyword] = filtered;
			}
		}

		Register(new[] { "sales", "sale", "revenue", "kita", "benta" }, "sales", "orders", "orderitems");
		Register(new[] { "order", "orders", "transaction", "checkout", "receipt" }, "orders", "orderitems", "sales");
		Register(new[] { "product", "products", "produkto", "inventory", "stock", "item", "items" }, "products", "productcategory", "orderitems", "defectiveitems");
		Register(new[] { "category", "categories" }, "productcategory", "categories");
		Register(new[] { "defect", "defective", "return", "returns" }, "defectiveitems", "products", "suppliers");
		Register(new[] { "supplier", "suppliers", "vendor", "vendors" }, "suppliers", "products", "defectiveitems");
		Register(new[] { "customer", "customers", "client", "clients", "contact", "contacts" }, "contacts", "orders", "sales");
		Register(new[] { "expense", "expenses", "gastos", "spending", "cost" }, "expenses", "expense_labels", "categories", "contacts");
		Register(new[] { "label", "labels", "tag", "tags" }, "expense_labels", "labels");
		Register(new[] { "budget", "budgets", "allocation" }, "budget", "budgethistory");
		Register(new[] { "plan", "planner", "scheduled", "schedule" }, "planned_payments", "planned_recurrence");
		Register(new[] { "attachment", "attachments", "receipt", "receipts" }, "attachments");

		return map;
	}

	private static string[] BuildDefaultFallbackTables(ISet<string> availableTables)
	{
		var defaults = new[] { "sales", "orders", "products", "expenses", "contacts" };
		return defaults
			.Select(t => t.ToLowerInvariant())
			.Where(availableTables.Contains)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
	}

	private HashSet<string> LoadAvailableTables()
	{
		var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		try
		{
			var connectionString = _configuration.GetConnectionString("DefaultConnection");
			if (string.IsNullOrWhiteSpace(connectionString))
			{
				_logger.LogWarning("DefaultConnection is not configured; schema mapping will be empty.");
				return tables;
			}

			using var connection = new NpgsqlConnection(connectionString);
			connection.Open();

			const string sql = """
				SELECT tablename
				FROM pg_catalog.pg_tables
				WHERE schemaname = 'public'
			""";

			using var cmd = new NpgsqlCommand(sql, connection);
			using var reader = cmd.ExecuteReader();

			while (reader.Read())
			{
				var tableName = reader.GetString(0);
				if (!string.IsNullOrWhiteSpace(tableName))
				{
					tables.Add(tableName.ToLowerInvariant());
				}
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to load available table names for schema mapping.");
		}

		return tables;
	}

	private static List<string> ExtractKeywords(string query)
	{
		if (string.IsNullOrWhiteSpace(query))
		{
			return new List<string>();
		}

		var words = query.ToLowerInvariant()
			.Split(new[] { ' ', ',', '.', '?', '!', ';', ':', '\n', '\r' },
				StringSplitOptions.RemoveEmptyEntries);

		var stopWords = new HashSet<string>
		{
			"ang", "ng", "sa", "ay", "na", "at", "ko", "mo", "natin", "namin",
			"the", "a", "an", "and", "or", "but", "in", "on", "at", "to", "for",
			"of", "with", "by", "from", "up", "about", "into", "through", "during",
			"what", "how", "many", "much", "show", "me", "po", "natin", "yung", "yan"
		};

		return words
			.Where(w => w.Length > 2 && !stopWords.Contains(w))
			.Distinct()
			.ToList();
	}

	private HashSet<string> GetRelevantTables(IEnumerable<string> keywords)
	{
		Dictionary<string, string[]> keywordMap;
		HashSet<string> available;

		lock (_syncRoot)
		{
			keywordMap = _keywordToTables;
			available = _availableTables;
		}

		var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var keyword in keywords)
		{
			if (keywordMap.TryGetValue(keyword, out var mappedTables))
			{
				foreach (var table in mappedTables)
				{
					tables.Add(table);
				}
			}

			if (available.Contains(keyword))
			{
				tables.Add(keyword);
			}
			else if (keyword.EndsWith("s", StringComparison.OrdinalIgnoreCase))
			{
				var singular = keyword[..^1];
				if (available.Contains(singular))
				{
					tables.Add(singular);
				}
			}
		}

		return tables;
	}
}
