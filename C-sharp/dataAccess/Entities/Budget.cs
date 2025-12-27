using System;
using System.ComponentModel.DataAnnotations;

namespace dataAccess.Entities
{
    public class Budget
    {
        public int BudgetId { get; set; }
        public DateOnly MonthYear { get; set; }
        public decimal? MonthlyBudgetAmount { get; set; }
        public DateTime CreatedAt { get; set; }
        public int? BusinessId { get; set; }
    }
}
