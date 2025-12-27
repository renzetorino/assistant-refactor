using System;
using System.ComponentModel.DataAnnotations;

namespace dataAccess.Entities
{
    public class Label
    {
        public Guid Id { get; set; }
        public Guid? UserId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Color { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public int? BusinessId { get; set; }
    }
}
