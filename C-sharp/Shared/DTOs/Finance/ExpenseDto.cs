using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;


// UPDATE: MAP NAME 1:1 WITH DB COLUMNS


namespace Shared.DTOs.Finance
{
    public class ExpenseDto
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
        public string CreatedByUserId { get; set; }

    }
}
