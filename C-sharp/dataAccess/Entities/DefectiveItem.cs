namespace dataAccess.Entities
{
    public class DefectiveItem
    {
        public int DefectiveItemId { get; set; }
        public int ProductId { get; set; }
        public int ProductCategoryId { get; set; }
        public DateOnly ReportedDate { get; set; }
        public string? DefectDescription { get; set; }
        public int Quantity { get; set; }
        public string? Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public Guid? ReportedByUserId { get; set; }
        public int? BusinessId { get; set; }

        public Product? Product { get; set; }
    }
}
