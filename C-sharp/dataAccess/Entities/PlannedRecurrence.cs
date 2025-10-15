using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace dataAccess.Entities
{
    public class PlannedRecurrence
    {
        public Guid PlannedPaymentId { get; set; }
        public string Repeat { get; set; } = "interval"; // DB enum → string
        public int Every { get; set; } = 1;
        public string Duration { get; set; } = "open";   // DB enum → string
        public DateOnly? UntilDate { get; set; }
        public int? OccurrencesCount { get; set; }
    }
}

