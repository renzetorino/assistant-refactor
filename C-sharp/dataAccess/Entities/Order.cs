using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace dataAccess.Entities
{
    public class Order
    {
        [Key]
        public int OrderId { get; set; }

        public DateTime OrderDate { get; set; }

        [Range(typeof(decimal), "0", "79228162514264337593543950335")]
        public decimal TotalAmount { get; set; }

        public string OrderStatus { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public decimal AmountPaid { get; set; }
        public decimal Change { get; set; }
        public Guid? UserId { get; set; }
        public int? BusinessId { get; set; }
        public string? OrderCode { get; set; }

        public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
    }
}
