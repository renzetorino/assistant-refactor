using System;
using System.Security.Claims;
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
            // Extract user_id from "sub" claim (standard JWT claim)
            // Try both "sub" (short form) and ClaimTypes.NameIdentifier (mapped form)
            var userIdClaim = context.User.FindFirst("sub")?.Value 
                ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            
            if (!string.IsNullOrWhiteSpace(userIdClaim) && Guid.TryParse(userIdClaim, out var userId))
            {
                context.Items["UserId"] = userId;
                _logger.LogDebug("Extracted UserId: {UserId} from JWT", userId);
            }
            else
            {
                _logger.LogWarning("Failed to extract valid UserId from JWT claims");
            }

            // Extract business_id from custom claim (REQUIRED)
            // Note: This requires Supabase JWT to include business_id claim
            var businessIdClaim = context.User.FindFirst("business_id")?.Value;
            if (!string.IsNullOrWhiteSpace(businessIdClaim) && int.TryParse(businessIdClaim, out var businessId))
            {
                context.Items["BusinessId"] = businessId;
                _logger.LogDebug("Extracted BusinessId: {BusinessId} from JWT", businessId);
            }
            else
            {
                // ❌ SECURITY: business_id is REQUIRED for all authenticated requests
                _logger.LogWarning("Missing required business_id claim in JWT - request denied");
                
                context.Response.StatusCode = 403;
                await context.Response.WriteAsync("Forbidden: Business context required for multi-tenancy isolation");
                return; // ❌ STOP REQUEST - Do not proceed to endpoints
            }

            // Log for security audit
            var endpoint = context.Request.Path.Value;
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
