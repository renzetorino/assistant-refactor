using System;
using System.Collections.Generic;

namespace dataAccess.Contracts
{
    /// <summary>
    /// Result of a complete insight scan for one business.
    /// </summary>
    public sealed class InsightScanResult
    {
        /// <summary>
        /// Business ID that was scanned.
        /// </summary>
        public int BusinessId { get; set; }

        /// <summary>
        /// Detector version/hash used for this scan (for cache invalidation).
        /// </summary>
        public string DetectorVersion { get; set; } = "v1.0.0";

        /// <summary>
        /// All findings discovered across all detectors.
        /// </summary>
        public List<DetectorFinding> Findings { get; set; } = new();

        /// <summary>
        /// Timestamp when the scan started.
        /// </summary>
        public DateTime ScanStartedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Timestamp when the scan completed.
        /// </summary>
        public DateTime? ScanCompletedAt { get; set; }

        /// <summary>
        /// Total duration of the scan in milliseconds.
        /// </summary>
        public long DurationMs { get; set; }

        /// <summary>
        /// Whether the scan completed successfully.
        /// </summary>
        public bool Success { get; set; } = true;

        /// <summary>
        /// Error message if scan failed.
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Number of detectors executed.
        /// </summary>
        public int DetectorsExecuted { get; set; }

        /// <summary>
        /// Number of detectors that failed.
        /// </summary>
        public int DetectorsFailed { get; set; }
    }
}
