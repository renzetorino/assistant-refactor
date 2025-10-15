using System;
using System.Text.Json.Serialization;

namespace embeddingSync.Models
{
    public class ProductRecord
    {
        [JsonPropertyName("productid")]
        public int ProductId { get; set; }

        [JsonPropertyName("productname")]
        public string ProductName { get; set; }

        [JsonPropertyName("description")]
        public string ProductDescription { get; set; }

        [JsonPropertyName("price")]
        public decimal ProductPrice { get; set; }

        [JsonPropertyName("cost")]
        public decimal ProductCost { get; set; }

        [JsonPropertyName("currentstock")]
        public int CurrentStock { get; set; }

        [JsonPropertyName("reorderpoint")]
        public int ReorderPoint { get; set; }

        [JsonPropertyName("age")]
        public string ProductAge { get; set; }

        [JsonPropertyName("supplierid")]
        public int SupplierId { get; set; }

        [JsonPropertyName("createdat")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("updatedat")]
        public DateTime UpdatedAt { get; set; }

        [JsonPropertyName("updatedstock")]
        public DateTime? UpdatedStock { get; set; }

        [JsonPropertyName("image_url")]
        public string ImageUrl { get; set; }

        [JsonPropertyName("updatedbyuserid")]
        public int? UpdatedByUserId { get; set; }
    }
}