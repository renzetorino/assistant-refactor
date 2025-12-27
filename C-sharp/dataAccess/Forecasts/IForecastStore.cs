using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace dataAccess.Forecasts
{
    /// <summary>
    /// Persists forecasts to the AI DB (VEC) → public.forecasts (plural).
    /// </summary>
    public interface IForecastStore
    {
        /// <summary>
        /// Multi-tenancy aware save method with userId and businessId
        /// </summary>
        /// <param name="userId">User ID for multi-tenancy scoping</param>
        /// <param name="businessId">Business ID for multi-tenancy scoping (optional)</param>
        /// <param name="domain">"sales" or "expenses"</param>
        /// <param name="target">"overall" or another identifier</param>
        /// <param name="horizonDays">REQUIRED (NOT NULL in your schema)</param>
        /// <param name="@params">jsonb params (e.g., { start, end, label })</param>
        /// <param name="result">jsonb forecast payload (UI spec / forecast data)</param>
        /// <param name="status">default "queued" or "done"</param>
        Task<Guid> SaveAsync(
            Guid userId,
            int? businessId,
            string domain,
            string target,
            int horizonDays,
            JsonObject @params,
            JsonObject result,
            string status = "queued",
            CancellationToken ct = default);

        /// <summary>
        /// Legacy method without multi-tenancy (for backward compatibility)
        /// </summary>
        Task<Guid> SaveAsync(
            string domain,
            string target,
            int horizonDays,
            JsonObject @params,
            JsonObject result,
            string status = "queued",
            CancellationToken ct = default);

        Task<ForecastRow?> GetAsync(Guid id, CancellationToken ct = default);

        /// <summary>
        /// Multi-tenancy aware recent method with userId and businessId
        /// </summary>
        Task<ForecastRow[]> RecentAsync(Guid userId, int? businessId, string domain, int limit = 10, CancellationToken ct = default);

        /// <summary>
        /// Legacy method without multi-tenancy (for backward compatibility)
        /// </summary>
        Task<ForecastRow[]> RecentAsync(string domain, int limit = 10, CancellationToken ct = default);
    }

    public sealed record ForecastRow(
        Guid Id,
        string Domain,
        string Target,
        int HorizonDays,
        string Status,
        JsonObject Params,
        JsonObject? Result,
        DateTime CreatedAt,
        DateTime UpdatedAt
    );
}
