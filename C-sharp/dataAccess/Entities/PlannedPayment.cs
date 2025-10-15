using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace dataAccess.Entities
{
    public class PlannedPayment
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string Name { get; set; } = "";
        public Guid? CategoryId { get; set; }
        public decimal Amount { get; set; }
        public Guid? ContactId { get; set; }
        public string Frequency { get; set; } = "monthly"; // DB enum → string
        public DateOnly? DueDate { get; set; }
        public string? Notes { get; set; }
        public Guid? LabelId { get; set; }
        public string Notify { get; set; } = "none";       // DB enum → string
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public Guid? ExpenseId { get; set; }
        public DateTime? CompletedAt { get; set; }
    }
}

