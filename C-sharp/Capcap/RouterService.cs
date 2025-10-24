using Capcap.Services;
using dataAccess.Services;                  // SqlQueryService, VectorSearchService, HybridQueryService
using System.Text.RegularExpressions;
using System.Diagnostics;

namespace Capcap;

public enum RouteKind { ChitChat, Sql, Vector, Hybrid }

public sealed class RouterService
{
    private readonly IChatLlm _llm;
    private readonly SqlQueryService _sql;
    private readonly VectorSearchService _vec;
    private readonly HybridQueryService _hyb;

    public RouterService(IChatLlm llm, SqlQueryService sql, VectorSearchService vec, HybridQueryService hyb)
    {
        _llm = llm;
        _sql = sql;
        _vec = vec;
        _hyb = hyb;
    }

    public async Task<object> HandleAsync(string input, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        // 1) Obvious chit-chat fast path
        var hk = HeuristicRoute(input);
        if (hk == RouteKind.ChitChat)
        {
            var reply = await ChatAsync(input, ct);
            sw.Stop();
            return new { route = "chitchat", data = new { reply }, error = (object?)null, meta = new { ms = sw.ElapsedMilliseconds } };
        }

        // 2) LLM classification (strict token)
        var route = await ClassifyAsync(input, ct);

        try
        {
            switch (route)
            {
                case RouteKind.Sql:
                    {
                        var result = await _sql.DispatchAsync(input, ct);
                        sw.Stop();
                        return new { route = "sql", data = result, error = (object?)null, meta = new { ms = sw.ElapsedMilliseconds } };
                    }
                case RouteKind.Vector:
                    {
                        var result = await _vec.DispatchAsync(input, ct);
                        sw.Stop();
                        return new { route = "vector", data = result, error = (object?)null, meta = new { ms = sw.ElapsedMilliseconds } };
                    }
                case RouteKind.Hybrid:
                    {
                        var result = await _hyb.DispatchAsync(input, ct);
                        sw.Stop();
                        return new { route = "hybrid", data = result, error = (object?)null, meta = new { ms = sw.ElapsedMilliseconds } };
                    }
                case RouteKind.ChitChat:
                default:
                    {
                        var reply = await ChatAsync(input, ct);
                        sw.Stop();
                        return new { route = "chitchat", data = new { reply }, error = (object?)null, meta = new { ms = sw.ElapsedMilliseconds } };
                    }
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            // Surface a clean error payload instead of 500
            return new { route = route.ToString().ToLowerInvariant(), data = (object?)null, error = new { message = ex.Message }, meta = new { ms = sw.ElapsedMilliseconds } };
        }
    }

    private async Task<RouteKind> ClassifyAsync(string input, CancellationToken ct)
    {
        var system = "You are a strict router. Return ONLY one token: chitchat, sql, vector, or hybrid.";
        var reply = await _llm.ClassifyAsync(system, input, ct);
        var norm = (reply ?? "").Trim().ToLowerInvariant();

        return norm switch
        {
            "sql" => RouteKind.Sql,
            "vector" => RouteKind.Vector,
            "hybrid" => RouteKind.Hybrid,
            "chat" or "chitchat" or "conversation" => RouteKind.ChitChat,
            _ => RouteKind.ChitChat
        };
    }

    private Task<string> ChatAsync(string user, CancellationToken ct)
    {
        const string system = "You are a helpful, concise assistant for BuizwAIz.";
        return _llm.ChatAsync(system, user, ct);
    }

    private static RouteKind HeuristicRoute(string input)
    {
        var q = input.Trim();
        // Very simple chit-chat detection
        if (Regex.IsMatch(q, @"^(hi|hello|hey|sup|good (morning|evening)|how (are|r) (you|u))\b", RegexOptions.IgnoreCase))
            return RouteKind.ChitChat;

        // For anything data-ish, let LLM decide SQL/Vector/Hybrid
        return RouteKind.Hybrid;
    }
}
