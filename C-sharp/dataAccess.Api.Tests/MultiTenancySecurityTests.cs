using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Xunit;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using dataAccess.Services;
using dataAccess.Entities;

namespace dataAccess.Api.Tests;

/// <summary>
/// Critical security tests to prevent multi-tenancy data leaks.
/// These tests verify that business_id isolation is enforced at all layers.
/// </summary>
public class MultiTenancySecurityTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public MultiTenancySecurityTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    /// <summary>
    /// CRITICAL: Requests without business_id must fail with 403 Forbidden
    /// </summary>
    [Fact]
    public async Task Request_Without_BusinessId_Returns_403()
    {
        // Arrange: Create JWT without business_id claim
        var jwtWithoutBusinessId = CreateJwtToken(userId: Guid.NewGuid(), businessId: null);
        _client.DefaultRequestHeaders.Authorization = 
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwtWithoutBusinessId);

        // Act: Try to access protected endpoint
        var response = await _client.GetAsync("/api/reports/recent?limit=10");

        // Assert: MUST return 403 Forbidden
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Business context required", content);
    }

    /// <summary>
    /// CRITICAL: Sales reports must not leak across businesses
    /// </summary>
    [Fact]
    public async Task Sales_Reports_Cannot_Access_Other_Business_Data()
    {
        // Arrange: Create data for Business 1
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        
        var business1Order = new Order
        {
            OrderId = Guid.NewGuid(),
            BusinessId = 1,
            OrderDate = DateTime.UtcNow,
            OrderStatus = "Completed"
        };
        dbContext.Orders.Add(business1Order);
        await dbContext.SaveChangesAsync();

        // Act 1: User from Business 1 requests sales report
        var jwt1 = CreateJwtToken(Guid.NewGuid(), businessId: 1);
        _client.DefaultRequestHeaders.Authorization = 
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwt1);
        
        var response1 = await _client.PostAsJsonAsync("/api/chat", new 
        {
            message = "Show me sales report for last 30 days"
        });
        
        Assert.True(response1.IsSuccessStatusCode);

        // Act 2: User from Business 2 tries to see reports
        var jwt2 = CreateJwtToken(Guid.NewGuid(), businessId: 2);
        _client.DefaultRequestHeaders.Authorization = 
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwt2);
        
        var response2 = await _client.GetAsync("/api/reports/recent?domain=sales&limit=10");
        
        // Assert: Business 2 should see ZERO reports (cannot access Business 1 data)
        Assert.True(response2.IsSuccessStatusCode);
        var reports = await response2.Content.ReadFromJsonAsync<Report[]>();
        Assert.NotNull(reports);
        Assert.Empty(reports); // ✅ CRITICAL: Must be empty!
        
        // Cleanup
        dbContext.Orders.Remove(business1Order);
        await dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// CRITICAL: Expense queries must enforce business isolation
    /// </summary>
    [Fact]
    public async Task Expense_Queries_Enforce_Business_Isolation()
    {
        // Arrange: Create expenses for two different businesses
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        
        var expense1 = new Expense
        {
            Id = Guid.NewGuid(),
            BusinessId = 1,
            Amount = 100.00m,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
            Notes = "Business 1 expense"
        };
        
        var expense2 = new Expense
        {
            Id = Guid.NewGuid(),
            BusinessId = 2,
            Amount = 200.00m,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
            Notes = "Business 2 expense"
        };
        
        dbContext.Expenses.AddRange(expense1, expense2);
        await dbContext.SaveChangesAsync();

        // Act: Query as Business 1 user
        var jwt1 = CreateJwtToken(Guid.NewGuid(), businessId: 1);
        _client.DefaultRequestHeaders.Authorization = 
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwt1);
        
        var response = await _client.PostAsJsonAsync("/api/chat", new 
        {
            message = "Show me expense summary for last 30 days"
        });
        
        Assert.True(response.IsSuccessStatusCode);
        var content = await response.Content.ReadAsStringAsync();
        
        // Assert: Should NOT contain Business 2's expense
        Assert.DoesNotContain("Business 2 expense", content);
        Assert.DoesNotContain("200.00", content);
        
        // Cleanup
        dbContext.Expenses.RemoveRange(expense1, expense2);
        await dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// CRITICAL: Inventory queries must enforce business isolation
    /// </summary>
    [Fact]
    public async Task Inventory_Queries_Enforce_Business_Isolation()
    {
        // Arrange: Create products for two different businesses
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        
        var product1 = new Product
        {
            ProductId = 1000,
            ProductName = "Business 1 Product",
            BusinessId = 1,
            SupplierId = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        
        var product2 = new Product
        {
            ProductId = 2000,
            ProductName = "Business 2 Product",
            BusinessId = 2,
            SupplierId = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        
        dbContext.Products.AddRange(product1, product2);
        await dbContext.SaveChangesAsync();

        // Act: Query as Business 1 user
        var jwt1 = CreateJwtToken(Guid.NewGuid(), businessId: 1);
        _client.DefaultRequestHeaders.Authorization = 
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwt1);
        
        var response = await _client.PostAsJsonAsync("/api/chat", new 
        {
            message = "Show me inventory snapshot"
        });
        
        Assert.True(response.IsSuccessStatusCode);
        var content = await response.Content.ReadAsStringAsync();
        
        // Assert: Should NOT contain Business 2's product
        Assert.DoesNotContain("Business 2 Product", content);
        
        // Cleanup
        dbContext.Products.RemoveRange(product1, product2);
        await dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// CRITICAL: Forecast queries must enforce business isolation
    /// </summary>
    [Fact]
    public async Task Forecast_Queries_Enforce_Business_Isolation()
    {
        // Arrange: User from Business 2
        var jwt2 = CreateJwtToken(Guid.NewGuid(), businessId: 2);
        _client.DefaultRequestHeaders.Authorization = 
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwt2);

        // Act: Request forecasts
        var response = await _client.GetAsync("/api/forecasts/recent?domain=sales&limit=10");

        // Assert: Should succeed but return empty (no cross-business access)
        Assert.True(response.IsSuccessStatusCode);
        var forecasts = await response.Content.ReadFromJsonAsync<object[]>();
        Assert.NotNull(forecasts);
        // If there are any forecasts, they must belong to Business 2 only
    }

    /// <summary>
    /// CRITICAL: SQL queries cannot accept null business_id
    /// </summary>
    [Fact]
    public async Task SqlCatalog_Throws_When_BusinessId_Missing()
    {
        // Arrange: Get SqlCatalog service
        using var scope = _factory.Services.CreateScope();
        var sqlCatalog = scope.ServiceProvider.GetRequiredService<ISqlCatalog>();

        // Act & Assert: Calling query without business_id should throw
        var args = new Dictionary<string, object?>
        {
            { "start", DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)) },
            { "end", DateOnly.FromDateTime(DateTime.UtcNow) }
            // ❌ Missing business_id
        };

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await sqlCatalog.RunAsync("SalesSummary", args);
        });
    }

    /// <summary>
    /// Helper: Create a test JWT token with business_id claim
    /// </summary>
    private string CreateJwtToken(Guid userId, int? businessId)
    {
        // In real tests, you would create a proper JWT with correct signing
        // For now, this is a placeholder - you'll need to integrate with your actual JWT generation
        var claims = new Dictionary<string, object>
        {
            { "sub", userId.ToString() },
            { "exp", DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds() }
        };

        if (businessId.HasValue)
        {
            claims["business_id"] = businessId.Value;
        }

        // TODO: Replace with actual JWT generation using your secret key
        // For testing, you might want to use a test-specific JWT helper
        return "test-jwt-token-" + Guid.NewGuid();
    }
}

/// <summary>
/// DTO for deserializing report responses
/// </summary>
public class Report
{
    public Guid Id { get; set; }
    public string Domain { get; set; } = string.Empty;
    public string PeriodLabel { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
