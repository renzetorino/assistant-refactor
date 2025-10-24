using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Shared.DTOs.Sales
{
    public class OrderItemDto
    {
        [JsonPropertyName("orderitemid")]
        public int OrderItemId { get; set; }

        [JsonPropertyName("orderid")]
        public int OrderId { get; set; }

        [JsonPropertyName("productid")]
        public int ProductId { get; set; }

        [JsonPropertyName("quantity")]
        public int Quantity { get; set; }

        [JsonPropertyName("unitprice")]
        public decimal UnitPrice { get; set; }

        [JsonPropertyName("subtotal")]
        public decimal Subtotal { get; set; }

        [JsonPropertyName("createdat")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("updatedat")]
        public DateTime UpdatedAt { get; set; }
    }
}
