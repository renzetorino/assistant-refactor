using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Shared.DTOs.Procurement
{
    public class SupplierDto
    {
        [JsonPropertyName("supplierid")]
        public int SupplierId { get; set; }

        [JsonPropertyName("suppliername")]
        public string SupplierName { get; set; } = string.Empty;

        [JsonPropertyName("contactperson")]
        public string? ContactPerson { get; set; }

        [JsonPropertyName("phonenumber")]
        public string? PhoneNumber { get; set; }

        [JsonPropertyName("supplieremail")]
        public string? SupplierEmail { get; set; }

        [JsonPropertyName("address")]
        public string? Address { get; set; }

        [JsonPropertyName("createdat")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("updatedat")]
        public DateTime UpdatedAt { get; set; }

        [JsonPropertyName("supplierstatus")]
        public string? SupplierStatus { get; set; }

        [JsonPropertyName("defectreturned")]
        public int? DefectReturned { get; set; }
    }
}
