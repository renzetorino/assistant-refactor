using System;
using System.Text.Json.Nodes;

namespace dataAccess.Entities
{
    /// <summary>
    /// Represents a forecast stored in the AI database (public.forecasts table).
    /// Includes user_id and business_id for multi-tenancy scoping.
    /// </summary>
    public class Forecast
    {
        public Guid Id { get; set; }
        
        // Multi-tenancy fields
        public Guid UserId { get; set; }
        public int? BusinessId { get; set; }
        
        // Forecast metadata
        public string Domain { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
        public int HorizonDays { get; set; }
        
        // Configuration and results (stored as JSONB)
        public string Params { get; set; } = "{}";
        public string Status { get; set; } = "queued";
        public string? Result { get; set; }
        
        // Timestamps
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
