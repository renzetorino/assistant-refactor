using System;

namespace dataAccess.Entities
{
    /// <summary>
    /// Entity representing activity log entries for tracking business data changes.
    /// Used for cache invalidation in AI Insights system.
    /// </summary>
    public class ActivityLog
    {
        public int Id { get; set; }
        
        public string ActionType { get; set; } = string.Empty;
        
        public string ActionDesc { get; set; } = string.Empty;
        
        public string? DoneUser { get; set; }
        
        public int BusinessId { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
