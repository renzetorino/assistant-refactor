using System;
using System.Text.Json.Serialization;

namespace embeddingSync.Models
{
    public class ExpenseRecord
    {
        [JsonPropertyName("expenseid")]
        public int ExpenseId { get; set; }

        [JsonPropertyName("expensedate")]
        public DateTime ExpenseDate { get; set; }

        [JsonPropertyName("amount")]
        public decimal ExpenseAmount { get; set; }

        [JsonPropertyName("description")]
        public string ExpenseDescription { get; set; }

        [JsonPropertyName("category")]
        public string ExpenseCategory { get; set; }

        [JsonPropertyName("createdat")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("updatedat")]
        public DateTime UpdatedAt { get; set; }

        [JsonPropertyName("createdbyuserid")]
        public string CreatedByUserId { get; set; } // or Guid if strongly typed
    }
}
