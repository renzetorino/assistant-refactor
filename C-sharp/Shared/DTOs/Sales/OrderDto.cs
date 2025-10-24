using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Shared.DTOs.Sales
{
    public class OrderDto
    {
        [JsonPropertyName("orderid")]
        public int OrderId { get; set; }

        [JsonPropertyName("orderdate")]
        public DateTime OrderDate { get; set; }

        [JsonPropertyName("totalamount")]
        public decimal TotalAmount { get; set; }

        [JsonPropertyName("orderstatus")]
        public string OrderStatus { get; set; }

        [JsonPropertyName("createdat")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("updatedat")]
        public DateTime UpdatedAt { get; set; }
    }
}
