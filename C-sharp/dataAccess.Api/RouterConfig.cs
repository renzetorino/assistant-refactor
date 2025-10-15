// dataAccess.Api/RouterConfig.cs
using System.Collections.Generic;

namespace dataAccess.Api
{
    public sealed class RouterConfig
    {
        public List<DomainRule> Intents { get; set; } = new();
        public string? IntentFallback { get; set; }
        public List<DomainRule> Domains { get; set; } = new();
        public string? DomainFallback { get; set; }
    }

    public sealed class DomainRule
    {
        public string Id { get; set; } = "";
        public List<string> Any { get; set; } = new();
        public List<string> Not { get; set; } = new();
        public double Weight { get; set; } = 1.0;
        public double Threshold { get; set; } = 1.0;
    }
}
