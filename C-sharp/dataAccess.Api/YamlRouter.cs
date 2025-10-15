// dataAccess.Api/YamlRouter.cs
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace dataAccess.Api
{
    public interface ITextRouter
    {
        (string intent, string? domain, double confidence) Route(string text);
    }

    public sealed class YamlRouter : ITextRouter
    {
        private readonly RouterConfig _cfg;
        public YamlRouter(RouterConfig cfg) => _cfg = cfg;

        public (string intent, string? domain, double confidence) Route(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return (_cfg.IntentFallback ?? "nlq", null, 0);

            var t = text.ToLowerInvariant();

            // Stage A: intent
            var (intent, iScore) = Pick(_cfg.Intents, t, _cfg.IntentFallback ?? "nlq");

            // Stage B: domain (for report/forecasting only)
            string? domain = null;
            double dScore = 0;
            if (intent is "report" or "forecasting")
                (domain, dScore) = Pick(_cfg.Domains, t, _cfg.DomainFallback ?? "sales");

            var raw = System.Math.Max(iScore, dScore);
            var conf = raw >= 1 ? 1.0 : 0.0;
            return (intent, domain, conf);
        }

        private static (string pick, double score) Pick(IEnumerable<DomainRule> rules, string text, string fallback)
        {
            double best = double.NegativeInfinity;
            string pick = fallback;

            foreach (var r in rules)
            {
                if (MatchesAny(text, r.Not)) continue;

                var score = 0.0;
                foreach (var pat in r.Any)
                    if (Contains(text, pat)) score += 1.0;

                score *= (r.Weight <= 0 ? 1.0 : r.Weight);

                if (score >= r.Threshold && score > best)
                {
                    best = score;
                    pick = r.Id;
                }
            }

            if (best < 0) best = 0;
            return (pick, best);
        }

        private static bool Contains(string text, string pat)
        {
            if (pat.StartsWith("regex:/") && pat.EndsWith("/"))
            {
                var rx = pat[6..^1];
                return Regex.IsMatch(text, rx, RegexOptions.IgnoreCase);
            }
            return text.Contains(pat.ToLowerInvariant());
        }

        private static bool MatchesAny(string text, IEnumerable<string> pats)
            => pats.Any(p => Contains(text, p));
    }
}
