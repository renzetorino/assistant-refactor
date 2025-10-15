using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace dataAccess.Entities
{
    public class Attachment
    {
        public Guid Id { get; set; }
        public Guid? UserId { get; set; }
        public Guid? ExpenseId { get; set; }
        public string? StorageKey { get; set; }
        public string? MimeType { get; set; }
        public int? SizeBytes { get; set; }
        public DateTime? UploadedAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}

