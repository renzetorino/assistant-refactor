using Capcap.Services;
using dataAccess.Services;
using Shared.Enums;
using System.Threading;
using System.Threading.Tasks;

namespace Capcap;

public sealed class QueryClassifier
{
    private readonly LightHeuristics _h; private readonly IChatLlm _llm;
    public QueryClassifier(LightHeuristics h, IChatLlm llm) { _h = h; _llm = llm; }

    public async Task<QueryType> ClassifyAsync(string input, CancellationToken ct)
    {
        if (_h.Match(input) is QueryType qt) return qt;
        var label = await _llm.ClassifyAsync(
            "You are a router. Output one of: Structured | Semantic | Hybrid | ChitChat.",
            $"Prompt:\n{input}", ct);
        return label switch
        {
            "Structured" => QueryType.Structured,
            "Semantic" => QueryType.Semantic,
            "Hybrid" => QueryType.Hybrid,
            _ => QueryType.ChitChat
        };
    }
}

public sealed class LightHeuristics
{
    public QueryType? Match(string s)
    {
        var x = s.ToLowerInvariant();
        if (x.StartsWith("sql:")) return QueryType.Structured;
        if (x.StartsWith("similar:") || x.Contains("find similar")) return QueryType.Semantic;
        if (x.Contains("compare") && x.Contains("similar")) return QueryType.Hybrid;
        if (x.Contains("total") || x.Contains("top ") || x.Contains("average") || x.Contains("trend")) return QueryType.Structured;
        return null;
    }
}

