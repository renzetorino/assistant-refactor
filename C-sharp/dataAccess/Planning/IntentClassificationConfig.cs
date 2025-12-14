// dataAccess/Planning/IntentClassificationConfig.cs
using System.Collections.Generic;

namespace dataAccess.Planning
{
    /// <summary>
    /// Configuration for intent classification safety and validation.
    /// Loaded from router.yaml intent_classification section.
    /// </summary>
    public sealed class IntentClassificationConfig
    {
        /// <summary>
        /// Minimum confidence threshold for all intents (except chitchat)
        /// </summary>
        public double MinConfidence { get; set; } = 0.60;

        /// <summary>
        /// Special threshold for chitchat intent (more lenient)
        /// </summary>
        public double ChitchatConfidence { get; set; } = 0.55;

        /// <summary>
        /// Per-intent confidence thresholds (overrides MinConfidence)
        /// </summary>
        public Dictionary<string, double> Thresholds { get; set; } = new();

        /// <summary>
        /// Security allowlist - only these intents are permitted
        /// </summary>
        public List<string> AllowedIntents { get; set; } = new();
    }
}
