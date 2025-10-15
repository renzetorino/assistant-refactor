namespace dataAccess.Entities
{
    public class Expense
    {
        public Guid Id { get; set; }
        public Guid? UserId { get; set; }
        public DateOnly OccurredOn { get; set; }
        public Guid? CategoryId { get; set; }
        public decimal Amount { get; set; }
        public string? Notes { get; set; }
        public string? Status { get; set; }
        public Guid? ContactId { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime CreatedAt { get; set; }

        // NEW
        public Guid? PlannedPaymentId { get; set; }
        public string? TaxJson { get; set; }

        public Category? CategoryRef { get; set; }
        public Contact? ContactRef { get; set; }
    }
}
