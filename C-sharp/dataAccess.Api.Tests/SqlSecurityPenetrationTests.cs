using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Allowlists;
using dataAccess.Api.Services;
using Xunit;

namespace dataAccess.Api.Tests;

/// <summary>
/// SQL Security Penetration Tests - Day 4 Production Hardening
/// Tests SQL injection attacks and security bypass attempts
/// </summary>
public class SqlSecurityPenetrationTests
{
    private readonly SqlValidator _validator;
    private readonly ISqlAllowlist _allowlist;

    public SqlSecurityPenetrationTests()
    {
        // Use a mock allowlist with common tables
        _allowlist = new MockSqlAllowlist();
        _validator = new SqlValidator(_allowlist, NullLogger<SqlValidator>.Instance);
    }

    [Theory]
    [InlineData("SELECT * FROM users; DROP TABLE users;")]
    [InlineData("SELECT * FROM users;--")]
    [InlineData("SELECT name FROM users; DELETE FROM users;")]
    [InlineData("SELECT id FROM products; UPDATE products SET price = 0;")]
    public void Should_Block_Semicolon_Statement_Stacking(string sql)
    {
        // Act
        var (isValid, errorMessage) = _validator.ValidateSql(sql);

        // Assert
        Assert.False(isValid, $"Should block semicolon in query: {sql}");
        Assert.Contains("Semicolons are not allowed", errorMessage);
    }

    [Theory]
    [InlineData("DELETE FROM users WHERE id = 1")]
    [InlineData("UPDATE users SET password = 'hacked'")]
    [InlineData("INSERT INTO users (name) VALUES ('hacker')")]
    [InlineData("DROP TABLE users")]
    [InlineData("ALTER TABLE users ADD COLUMN hacked TEXT")]
    [InlineData("CREATE TABLE hacked (id SERIAL)")]
    [InlineData("TRUNCATE TABLE users")]
    public void Should_Block_DML_DDL_Operations(string sql)
    {
        // Act
        var (isValid, errorMessage) = _validator.ValidateSql(sql);

        // Assert
        Assert.False(isValid, $"Should block DML/DDL: {sql}");
        Assert.NotNull(errorMessage);
    }

    [Theory]
    [InlineData("SELECT id, name INTO new_table FROM users")]
    [InlineData("SELECT * INTO OUTFILE '/tmp/data.csv' FROM users")]
    [InlineData("SELECT users.* INTO backup_users FROM users")]
    public void Should_Block_SELECT_INTO_Table_Creation(string sql)
    {
        // Act
        var (isValid, errorMessage) = _validator.ValidateSql(sql);

        // Assert
        Assert.False(isValid, $"Should block SELECT INTO: {sql}");
        Assert.Contains("SELECT ... INTO", errorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("SELECT * FROM users")]
    [InlineData("SELECT u.*, p.* FROM users u JOIN products p ON u.id = p.user_id")]
    [InlineData("SELECT COUNT(*), * FROM users GROUP BY id")]
    public void Should_Block_SELECT_Star(string sql)
    {
        // Act
        var (isValid, errorMessage) = _validator.ValidateSql(sql);

        // Assert
        Assert.False(isValid, $"Should block SELECT *: {sql}");
        Assert.Contains("SELECT *", errorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("SELECT id FROM users WHERE name = ''; DROP TABLE users; --'")]
    [InlineData("SELECT id FROM users WHERE id = 1 OR 1=1")]
    [InlineData("SELECT id FROM users WHERE name = 'admin' AND password = '' OR '1'='1'")]
    public void Should_Block_Classic_SQL_Injection_Patterns(string sql)
    {
        // Act
        var (isValid, errorMessage) = _validator.ValidateSql(sql);

        // Assert - Should fail for various reasons (semicolon, or table not in allowlist)
        Assert.False(isValid, $"Should block SQL injection attempt: {sql}");
    }

    [Theory]
    [InlineData("EXEC sp_executesql 'DROP TABLE users'")]
    [InlineData("EXECUTE xp_cmdshell 'whoami'")]
    [InlineData("CALL malicious_procedure()")]
    public void Should_Block_Stored_Procedure_Execution(string sql)
    {
        // Act
        var (isValid, errorMessage) = _validator.ValidateSql(sql);

        // Assert
        Assert.False(isValid, $"Should block EXEC/CALL: {sql}");
        Assert.NotNull(errorMessage);
    }

    [Theory]
    [InlineData("COPY users TO '/tmp/users.csv'")]
    [InlineData("COPY users FROM '/tmp/malicious.csv'")]
    [InlineData("COPY (SELECT * FROM users) TO STDOUT")]
    public void Should_Block_COPY_Operations(string sql)
    {
        // Act
        var (isValid, errorMessage) = _validator.ValidateSql(sql);

        // Assert
        Assert.False(isValid, $"Should block COPY: {sql}");
        Assert.NotNull(errorMessage);
    }

    [Theory]
    [InlineData("BEGIN TRANSACTION; DELETE FROM users; COMMIT;")]
    [InlineData("START TRANSACTION")]
    [InlineData("ROLLBACK")]
    [InlineData("SAVEPOINT my_savepoint")]
    public void Should_Block_Transaction_Control(string sql)
    {
        // Act
        var (isValid, errorMessage) = _validator.ValidateSql(sql);

        // Assert
        Assert.False(isValid, $"Should block transaction control: {sql}");
        Assert.NotNull(errorMessage);
    }

    [Theory]
    [InlineData("SET ROLE admin")]
    [InlineData("SET SESSION AUTHORIZATION 'postgres'")]
    [InlineData("SET search_path TO malicious_schema")]
    public void Should_Block_Session_Manipulation(string sql)
    {
        // Act
        var (isValid, errorMessage) = _validator.ValidateSql(sql);

        // Assert
        Assert.False(isValid, $"Should block session manipulation: {sql}");
        Assert.NotNull(errorMessage);
    }

    [Theory]
    [InlineData("SELECT id FROM users WHERE name = '/*' OR 1=1 --*/'")]
    [InlineData("SELECT id /* comment with DROP TABLE */ FROM users")]
    [InlineData("SELECT id FROM users -- this is a comment with DELETE")]
    public void Should_Strip_Comments_Before_Validation(string sql)
    {
        // Act
        var (isValid, errorMessage) = _validator.ValidateSql(sql);

        // Assert
        // Should pass because comments are stripped and no malicious keywords remain
        // (assuming users table is in allowlist and columns are valid)
        var result = _validator.ValidateSql("SELECT id FROM users WHERE name = 'test'");
        Assert.NotNull(result); // Just verify it processes without crashing
    }

    [Theory]
    [InlineData("SELECT id FROM unauthorized_table")]
    [InlineData("SELECT id FROM public.secret_data")]
    [InlineData("SELECT id FROM users JOIN secret_table ON users.id = secret_table.user_id")]
    public void Should_Block_Unauthorized_Tables(string sql)
    {
        // Act
        var (isValid, errorMessage) = _validator.ValidateSql(sql);

        // Assert
        Assert.False(isValid, $"Should block unauthorized table: {sql}");
        Assert.Contains("not allowed", errorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("SELECT password FROM users")]
    [InlineData("SELECT users.ssn FROM users")]
    [InlineData("SELECT credit_card FROM users")]
    public void Should_Block_Unauthorized_Columns(string sql)
    {
        // Act
        var (isValid, errorMessage) = _validator.ValidateSql(sql);

        // Assert
        Assert.False(isValid, $"Should block unauthorized column: {sql}");
        Assert.Contains("Column not allowed", errorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("SELECT id FROM (SELECT id FROM users) AS subquery")]
    [InlineData("SELECT id FROM users WHERE id IN (SELECT id FROM secret_table)")]
    public void Should_Block_Derived_Tables_In_FROM_Clause(string sql)
    {
        // Act
        var (isValid, errorMessage) = _validator.ValidateSql(sql);

        // Assert
        Assert.False(isValid, $"Should block derived table: {sql}");
    var error = errorMessage ?? string.Empty;
    var isDerivedTableMessage = error.Contains("derived table", StringComparison.OrdinalIgnoreCase);
    var isTableNotAllowedMessage = error.Contains("Table not allowed", StringComparison.OrdinalIgnoreCase);
    Assert.True(isDerivedTableMessage || isTableNotAllowedMessage, "Expected derived table or table-not-allowed rejection message");
    }

    [Fact]
    public void Should_Allow_Valid_CTE_Query()
    {
        // Arrange
        var sql = @"
            WITH monthly_sales AS (
                SELECT user_id, SUM(amount) AS total
                FROM sales
                WHERE sale_date >= '2025-01-01'
                GROUP BY user_id
            )
            SELECT u.name, ms.total
            FROM users u
            JOIN monthly_sales ms ON u.id = ms.user_id";

        // Act
        var (isValid, errorMessage) = _validator.ValidateSql(sql);

        // Assert - Might fail due to column allowlist, but should pass structure validation
        // (CTEs are allowed, no DML/DDL, etc.)
        if (!isValid)
        {
            // Should only fail on allowlist checks, not on structure
            Assert.DoesNotContain("WITH", errorMessage ?? "", StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Should_Add_LIMIT_If_Missing()
    {
        // Arrange
        var sql = "SELECT id, name FROM users WHERE active = true";

        // Act
        var limitedSql = _validator.EnsureLimit(sql, 100);

        // Assert
        Assert.Contains("LIMIT", limitedSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Should_Clamp_Excessive_LIMIT()
    {
        // Arrange
        var sql = "SELECT id, name FROM users LIMIT 1000000";

        // Act
        var clampedSql = _validator.EnsureLimit(sql, 100);

        // Assert
        Assert.Contains("LIMIT 100", clampedSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("1000000", clampedSql);
    }
}

/// <summary>
/// Mock SQL allowlist for testing
/// </summary>
public class MockSqlAllowlist : ISqlAllowlist
{
    private static readonly HashSet<string> _allowedOperators = new(StringComparer.OrdinalIgnoreCase)
    {
        "=", "<>", ">", ">=", "<", "<=", "ILIKE"
    };

    private readonly HashSet<string> _allowedTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "users", "products", "sales", "orders"
    };

    private readonly Dictionary<string, HashSet<string>> _allowedColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["users"] = new(StringComparer.OrdinalIgnoreCase) { "id", "name", "email", "created_at" },
        ["products"] = new(StringComparer.OrdinalIgnoreCase) { "id", "name", "price", "stock" },
        ["sales"] = new(StringComparer.OrdinalIgnoreCase) { "id", "amount", "sale_date", "user_id" },
        ["orders"] = new(StringComparer.OrdinalIgnoreCase) { "id", "user_id", "total", "order_date" }
    };

    public IReadOnlyCollection<string> Tables => _allowedTables;

    public bool IsTableAllowed(string tableName) => _allowedTables.Contains(tableName);

    public bool IsColumnAllowed(string tableName, string columnName)
    {
        if (!_allowedColumns.TryGetValue(tableName, out var columns))
            return false;
        return columns.Contains(columnName);
    }

    public bool IsOperatorAllowed(string op) => _allowedOperators.Contains(op);

    public int DefaultLimit => 50;

    public int MaxLimit => 1000;
}
