namespace dataAccess.Entities
{
    public class DefectiveItem
    {
        public int DefectiveItemId { get; set; }
        public int ProductId { get; set; }
        public int ProductCategoryId { get; set; }      // NEW
        public DateOnly ReportedDate { get; set; }      // NEW
        public string? DefectDescription { get; set; }  // fix column name
        public int Quantity { get; set; }
        public string? Status { get; set; }             // NEW
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public Guid? ReportedByUserId { get; set; }     // NEW

        public Product? Product { get; set; }
    }
}
