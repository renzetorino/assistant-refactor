using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Shared.DTOs.Catalog
{
    public class ProductCategoryDto
    {
        [JsonPropertyName("productcategoryid")]
        public int ProductCategoryId { get; set; }

        [JsonPropertyName("productid")]
        public int ProductId { get; set; }

        [JsonPropertyName("price")]
        public decimal Price { get; set; }

        [JsonPropertyName("cost")]
        public decimal Cost { get; set; }

        [JsonPropertyName("color")]
        public string? Color { get; set; }

        [JsonPropertyName("agesize")]
        public string? AgeSize { get; set; }

        [JsonPropertyName("currentstock")]
        public int CurrentStock { get; set; }

        [JsonPropertyName("reorderpoint")]
        public int? ReorderPoint { get; set; }

        [JsonPropertyName("updatedstock")]
        public DateTime? UpdatedStock { get; set; }
    }
}

