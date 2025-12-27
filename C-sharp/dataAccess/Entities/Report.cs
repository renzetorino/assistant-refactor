using System;

namespace dataAccess.Entities
{
    /// <summary>
    /// Represents a report stored in the AI database (public.reports table).
    /// Includes user_id and business_id for multi-tenancy scoping.
    /// </summary>
    public class Report
    {
        public Guid Id { get; set; }
        
        // Multi-tenancy fields
        public Guid UserId { get; set; }
        public int? BusinessId { get; set; }
        
        // Report metadata
        public string Domain { get; set; } = string.Empty;
        public string? Scope { get; set; }
        public string? ReportType { get; set; }
        public string? ProductId { get; set; }
        
        // Time period
        public DateOnly PeriodStart { get; set; }
        public DateOnly PeriodEnd { get; set; }
        public string PeriodLabel { get; set; } = string.Empty;
        
        // Configuration
        public bool CompareToPrior { get; set; }
        public short? TopK { get; set; }
        
        // YAML template info
        public string? YamlName { get; set; }
        public string? YamlVersion { get; set; }
        public string? ModelName { get; set; }
        
        // Report data (stored as JSONB)
        public string UiSpec { get; set; } = "{}";
        public string? Meta { get; set; }
        
        // Timestamps
        public DateTime CreatedAt { get; set; }
    }
}
