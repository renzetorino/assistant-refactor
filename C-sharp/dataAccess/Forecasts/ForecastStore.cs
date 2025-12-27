using dataAccess.Reports;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace dataAccess.Forecasts
{
    /// <summary>
    /// Uses VEC (APP__VEC__CONNECTIONSTRING) and writes/reads public.forecasts (plural).
    /// </summary>
    public sealed class ForecastStore : IForecastStore
    {
        private readonly string _vecConn;

        public ForecastStore(IConfiguration cfg)
        {
            // Use your existing resolver or read the env directly
            var resolver = new VecConnResolver(cfg);
            _vecConn = resolver.Resolve(); // resolves APP__VEC__CONNECTIONSTRING
            Console.WriteLine($"[ForecastStore] Using VEC: {VecConnResolver.Mask(_vecConn)}");
        }

        private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
        {
            var conn = new NpgsqlConnection(_vecConn);
            await conn.OpenAsync(ct);
            return conn;
        }

        public async Task<Guid> SaveAsync(
            Guid userId,
            int? businessId,
            string domain,
            string target,
            int horizonDays,
            JsonObject @params,
            JsonObject result,
            string status = "queued",
            CancellationToken ct = default)
        {
            Console.WriteLine(
                $"[MULTI-TENANCY] 💾 ForecastStore.SaveAsync | UserId: {userId}, BusinessId: {businessId?.ToString() ?? "NULL"}, Domain: {domain}, Target: {target}");

            await using var conn = await OpenAsync(ct);

            const string sql = @"
              insert into public.forecasts
                (user_id, business_id, domain, target, horizon_days, params, status, result)
              values
                (@user_id, @business_id, @domain, @target, @horizon_days, @params, @status, @result)
              returning id;";

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("user_id", userId);
            cmd.Parameters.AddWithValue("business_id", (object?)businessId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("domain", domain);
            cmd.Parameters.AddWithValue("target", target);
            cmd.Parameters.AddWithValue("horizon_days", horizonDays); // NOT NULL

            cmd.Parameters.Add("params", NpgsqlDbType.Jsonb).Value =
                @params is null ? "{}" : JsonSerializer.Serialize(@params);

            cmd.Parameters.AddWithValue("status", string.IsNullOrWhiteSpace(status) ? "queued" : status);

            cmd.Parameters.Add("result", NpgsqlDbType.Jsonb).Value =
                result is null ? (object?)DBNull.Value : JsonSerializer.Serialize(result);

            var idObj = await cmd.ExecuteScalarAsync(ct);
            var forecastId = (Guid)idObj!;

            Console.WriteLine(
                $"[MULTI-TENANCY] ✅ Forecast saved to database | ForecastId: {forecastId}, UserId: {userId}, BusinessId: {businessId?.ToString() ?? "NULL"}");

            return forecastId;
        }

        /// <summary>
        /// Legacy SaveAsync without multi-tenancy (for backward compatibility)
        /// </summary>
        public async Task<Guid> SaveAsync(
            string domain,
            string target,
            int horizonDays,
            JsonObject @params,
            JsonObject result,
            string status = "queued",
            CancellationToken ct = default)
        {
            // Call multi-tenancy version with empty Guid and null businessId
            return await SaveAsync(Guid.Empty, null, domain, target, horizonDays, @params, result, status, ct);
        }

        public async Task<ForecastRow?> GetAsync(Guid id, CancellationToken ct = default)
        {
            await using var conn = await OpenAsync(ct);

            const string sql = @"
              select id, domain, target, horizon_days, status, params, result, created_at, updated_at
              from public.forecasts
              where id = @id
              limit 1;";

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", id);

            await using var rdr = await cmd.ExecuteReaderAsync(ct);
            if (!await rdr.ReadAsync(ct)) return null;

            return new ForecastRow(
                rdr.GetGuid(0),
                rdr.GetString(1),
                rdr.GetString(2),
                rdr.GetInt32(3),
                rdr.GetString(4),
                ParseJsonObject(rdr, 5),
                ParseJsonObjectOrNull(rdr, 6),
                rdr.GetDateTime(7),
                rdr.GetDateTime(8)
            );
        }

        public async Task<ForecastRow[]> RecentAsync(Guid userId, int? businessId, string domain, int limit = 10, CancellationToken ct = default)
        {
            if (limit <= 0) limit = 10;
            await using var conn = await OpenAsync(ct);

            const string sql = @"
              select id, domain, target, horizon_days, status, params, result, created_at, updated_at
              from public.forecasts
              where domain = @domain
                and user_id = @user_id
                and (@business_id::int IS NULL OR business_id = @business_id)
              order by created_at desc
              limit @limit;";

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("domain", domain);
            cmd.Parameters.AddWithValue("user_id", userId);
            cmd.Parameters.AddWithValue("business_id", (object?)businessId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("limit", limit);

            var list = new List<ForecastRow>();
            await using var rdr = await cmd.ExecuteReaderAsync(ct);
            while (await rdr.ReadAsync(ct))
            {
                list.Add(new ForecastRow(
                    rdr.GetGuid(0),
                    rdr.GetString(1),
                    rdr.GetString(2),
                    rdr.GetInt32(3),
                    rdr.GetString(4),
                    ParseJsonObject(rdr, 5),
                    ParseJsonObjectOrNull(rdr, 6),
                    rdr.GetDateTime(7),
                    rdr.GetDateTime(8)
                ));
            }
            return list.ToArray();
        }

        /// <summary>
        /// Legacy RecentAsync without multi-tenancy (for backward compatibility)
        /// </summary>
        public async Task<ForecastRow[]> RecentAsync(string domain, int limit = 10, CancellationToken ct = default)
        {
            // Call multi-tenancy version with empty Guid and null businessId
            return await RecentAsync(Guid.Empty, null, domain, limit, ct);
        }

        private static JsonObject ParseJsonObject(NpgsqlDataReader rdr, int ordinal)
        {
            if (rdr.IsDBNull(ordinal)) return new JsonObject();
            var raw = rdr.GetFieldValue<string>(ordinal);
            try { return (JsonNode.Parse(raw) as JsonObject) ?? new JsonObject(); }
            catch { return new JsonObject(); }
        }

        private static JsonObject? ParseJsonObjectOrNull(NpgsqlDataReader rdr, int ordinal)
        {
            if (rdr.IsDBNull(ordinal)) return null;
            var raw = rdr.GetFieldValue<string>(ordinal);
            try { return (JsonNode.Parse(raw) as JsonObject) ?? new JsonObject(); }
            catch { return new JsonObject(); }
        }
    }
}
