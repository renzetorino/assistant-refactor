using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace dataAccess.Api.Middleware;

/// <summary>
/// Middleware that extracts user_id and business_id from JWT claims
/// and injects them into HttpContext.Items for use throughout the request pipeline.
/// This enables multi-tenancy scoping for all authenticated requests.
/// </summary>
public class BusinessScopingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<BusinessScopingMiddleware> _logger;

    public BusinessScopingMiddleware(RequestDelegate next, ILogger<BusinessScopingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only process authenticated requests
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            Console.WriteLine("\n╔═══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║         [MULTI-TENANCY] Business Scoping Middleware          ║");
            Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");

            // Extract user_id from "sub" claim (standard JWT claim)
            var userIdClaim = context.User.FindFirst("sub")?.Value;
            if (!string.IsNullOrWhiteSpace(userIdClaim) && Guid.TryParse(userIdClaim, out var userId))
            {
                context.Items["UserId"] = userId;
                Console.WriteLine($"✅ Extracted UserId from JWT: {userId}");
                _logger.LogDebug("Extracted UserId: {UserId} from JWT", userId);
            }
            else
            {
                Console.WriteLine("❌ Failed to extract valid UserId from JWT claims");
                _logger.LogWarning("Failed to extract valid UserId from JWT claims");
            }

            // Extract business_id from custom claim (if present)
            // Note: This requires Supabase JWT to include business_id claim
            var businessIdClaim = context.User.FindFirst("business_id")?.Value;
            if (!string.IsNullOrWhiteSpace(businessIdClaim) && int.TryParse(businessIdClaim, out var businessId))
            {
                context.Items["BusinessId"] = businessId;
                Console.WriteLine($"✅ Extracted BusinessId from JWT: {businessId}");
                _logger.LogDebug("Extracted BusinessId: {BusinessId} from JWT", businessId);
            }
            else
            {
                // business_id is optional for backward compatibility during migration
                // Services should handle null business_id gracefully
                Console.WriteLine("⚠️  No BusinessId found in JWT claims");
                Console.WriteLine("    ℹ️  This is expected if JWT custom claims hook is not configured yet");
                _logger.LogDebug("No BusinessId found in JWT claims (optional during migration)");
            }

            // Log for security audit
            var endpoint = context.Request.Path.Value;
            Console.WriteLine($"📍 Endpoint: {endpoint}");
            Console.WriteLine("═══════════════════════════════════════════════════════════════\n");

            _logger.LogInformation(
                "Business scope established for request: UserId={UserId}, BusinessId={BusinessId}, Endpoint={Endpoint}",
                context.Items.ContainsKey("UserId") ? context.Items["UserId"] : null,
                context.Items.ContainsKey("BusinessId") ? context.Items["BusinessId"] : null,
                endpoint
            );
        }

        await _next(context);
    }
}

/// <summary>
/// Extension method for easy middleware registration in Program.cs
/// </summary>
public static class BusinessScopingMiddlewareExtensions
{
    public static IApplicationBuilder UseBusinessScoping(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<BusinessScopingMiddleware>();
    }
}
