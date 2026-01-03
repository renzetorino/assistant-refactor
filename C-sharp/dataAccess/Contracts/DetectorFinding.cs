using System;

namespace dataAccess.Contracts
{
    /// <summary>
    /// Represents a single finding from a business insight detector.
    /// </summary>
    public sealed class DetectorFinding
    {
        /// <summary>
        /// Insight category (inventory, finance, sales, maturity).
        /// </summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>
        /// Severity level: info, warning, critical.
        /// </summary>
        public string Severity { get; set; } = string.Empty;

        /// <summary>
        /// Human-readable title for the finding.
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Detailed description or context about the finding.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Affected entity identifier (product ID, category name, etc.).
        /// </summary>
        public string? EntityId { get; set; }

        /// <summary>
        /// Affected entity name for display.
        /// </summary>
        public string? EntityName { get; set; }

        /// <summary>
        /// Metric value that triggered the finding (stock level, percentage, amount).
        /// </summary>
        public decimal? MetricValue { get; set; }

        /// <summary>
        /// Threshold that was crossed to trigger this finding.
        /// </summary>
        public decimal? ThresholdValue { get; set; }

        /// <summary>
        /// Additional metadata as JSON for extensibility.
        /// </summary>
        public string? Metadata { get; set; }

        /// <summary>
        /// Timestamp when this finding was detected.
        /// </summary>
        public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
    }
}
