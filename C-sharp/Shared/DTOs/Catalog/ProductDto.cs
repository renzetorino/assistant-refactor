using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Shared.DTOs.Catalog
{
    public class ProductDto
    {
        [JsonPropertyName("productid")]
        public int ProductId { get; set; }

        [JsonPropertyName("productname")]
        public string ProductName { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string? ProductDescription { get; set; } // or = string.Empty;

        [JsonPropertyName("supplierid")]
        public int SupplierId { get; set; }

        [JsonPropertyName("createdat")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("updatedat")]
        public DateTime UpdatedAt { get; set; }

        [JsonPropertyName("image_url")]
        public string ImageUrl { get; set; } = string.Empty;

        [JsonPropertyName("updatedbyuserid")]
        public Guid? UpdatedByUserId { get; set; } // <-- Guid?
    }
}

