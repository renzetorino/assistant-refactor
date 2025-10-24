using System;
using System.ComponentModel.DataAnnotations;

namespace dataAccess.Entities
{
    public class BudgetHistory
    {
        public long BudgetHistoryId { get; set; } // maps to "id" (bigint)
        public int? BudgetId { get; set; }        // maps to "budget_id"
        public decimal? OldAmount { get; set; }
        public decimal? NewAmount { get; set; }
        public DateTime CreatedAt { get; set; }

        public Budget? Budget { get; set; }
    }
}
