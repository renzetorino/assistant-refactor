using dataAccess.Entities;
using Microsoft.EntityFrameworkCore;

namespace dataAccess.Services
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options) { }

        // --- DbSets (Inventory/Sales) ---
        public DbSet<Supplier> Suppliers { get; set; } = default!;
        public DbSet<ProductCategory> ProductCategories { get; set; } = default!;
        public DbSet<Product> Products { get; set; } = default!;
        public DbSet<Order> Orders { get; set; } = default!;
        public DbSet<OrderItem> OrderItems { get; set; } = default!;
        public DbSet<DefectiveItem> DefectiveItems { get; set; } = default!;

        // --- Expense domain ---
        public DbSet<Expense> Expenses { get; set; } = default!;
        public DbSet<Category> Categories { get; set; } = default!;
        public DbSet<Contact> Contacts { get; set; } = default!;
        public DbSet<Label> Labels { get; set; } = default!;
        public DbSet<ExpenseLabel> ExpenseLabels { get; set; } = default!;
        public DbSet<Budget> Budgets { get; set; } = default!;
        public DbSet<BudgetHistory> BudgetHistories { get; set; } = default!;

        // --- New: Planner & Attachments ---
        public DbSet<PlannedPayment> PlannedPayments { get; set; } = default!;
        public DbSet<PlannedRecurrence> PlannedRecurrences { get; set; } = default!;
        public DbSet<Attachment> Attachments { get; set; } = default!;

        // Read-only projection for Sales reporting (view)
        public DbSet<Sales> Sales { get; set; } = default!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Default schema
            modelBuilder.HasDefaultSchema("public");

            // =========================
            // SALES PROJECTION (VIEW)
            // =========================
            modelBuilder.Entity<Sales>().ToView("sales");
            modelBuilder.Entity<Sales>().HasNoKey();

            // =========================
            // SUPPLIERS
            // =========================
            modelBuilder.Entity<Supplier>(e =>
            {
                e.ToTable("suppliers");
                e.HasKey(x => x.SupplierId);

                e.Property(x => x.SupplierId).HasColumnName("supplierid");
                e.Property(x => x.SupplierName).HasColumnName("suppliername");
                e.Property(x => x.ContactPerson).HasColumnName("contactperson");
                e.Property(x => x.PhoneNumber).HasColumnName("phonenumber");
                e.Property(x => x.SupplierEmail).HasColumnName("supplieremail");
                e.Property(x => x.Address).HasColumnName("address");
                e.Property(x => x.CreatedAt).HasColumnName("createdat");
                e.Property(x => x.UpdatedAt).HasColumnName("updatedat");
                e.Property(x => x.SupplierStatus).HasColumnName("supplierstatus");
                e.Property(x => x.DefectReturned).HasColumnName("defectreturned").IsRequired(false);
            });

            // =========================
            // PRODUCTS
            // =========================
            modelBuilder.Entity<Product>(e =>
            {
                e.ToTable("products");
                e.HasKey(p => p.ProductId);

                e.Property(p => p.ProductId).HasColumnName("productid");
                e.Property(p => p.ProductName).HasColumnName("productname");
                e.Property(p => p.Description).HasColumnName("description");
                e.Property(p => p.SupplierId).HasColumnName("supplierid");
                e.Property(p => p.CreatedAt).HasColumnName("createdat");
                e.Property(p => p.UpdatedAt).HasColumnName("updatedat");
                e.Property(p => p.ImageUrl).HasColumnName("image_url");
                e.Property(p => p.UpdatedByUserId).HasColumnName("updatedbyuserid");

                e.HasMany(p => p.OrderItems)
                    .WithOne(oi => oi.Product)
                    .HasForeignKey(oi => oi.ProductId)
                    .OnDelete(DeleteBehavior.Restrict);

                e.HasMany(p => p.DefectiveItems)
                    .WithOne(di => di.Product)
                    .HasForeignKey(di => di.ProductId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // =========================
            // PRODUCT CATEGORY / INVENTORY
            // =========================
            modelBuilder.Entity<ProductCategory>(e =>
            {
                e.ToTable("productcategory");
                e.HasKey(c => c.ProductCategoryId);

                e.Property(c => c.ProductCategoryId).HasColumnName("productcategoryid");
                e.Property(c => c.ProductId).HasColumnName("productid");
                e.Property(c => c.Price).HasColumnName("price");
                e.Property(c => c.Cost).HasColumnName("cost");
                e.Property(c => c.Color).HasColumnName("color").IsRequired(false);
                e.Property(c => c.AgeSize).HasColumnName("agesize").IsRequired(false);
                e.Property(c => c.CurrentStock).HasColumnName("currentstock");
                e.Property(c => c.ReorderPoint).HasColumnName("reorderpoint");
                e.Property(c => c.UpdatedStock).HasColumnName("updatedstock");
            });

            // =========================
            // ORDERS
            // =========================
            modelBuilder.Entity<Order>(b =>
            {
                b.ToTable("orders");
                b.HasKey(x => x.OrderId);

                b.Property(x => x.OrderId).HasColumnName("orderid");
                b.Property(x => x.OrderDate).HasColumnName("orderdate").HasColumnType("timestamp without time zone");
                b.Property(x => x.TotalAmount).HasColumnName("totalamount").HasPrecision(18, 2);
                b.Property(x => x.OrderStatus).HasColumnName("orderstatus");
                b.Property(x => x.CreatedAt).HasColumnName("createdat").HasColumnType("timestamp without time zone");
                b.Property(x => x.UpdatedAt).HasColumnName("updatedat").HasColumnType("timestamp without time zone");
                b.Property(x => x.AmountPaid).HasColumnName("amount_paid");
                b.Property(x => x.Change).HasColumnName("change");

                b.HasMany(o => o.OrderItems)
                    .WithOne(oi => oi.Order)
                    .HasForeignKey(oi => oi.OrderId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<OrderItem>(b =>
            {
                b.ToTable("orderitems");
                b.HasKey(x => x.OrderItemId);

                b.Property(x => x.OrderItemId).HasColumnName("orderitemid");
                b.Property(x => x.OrderId).HasColumnName("orderid");
                b.Property(x => x.ProductId).HasColumnName("productid");
                b.Property(x => x.ProductCategoryId).HasColumnName("productcategoryid");
                b.Property(x => x.Quantity).HasColumnName("quantity");
                b.Property(x => x.UnitPrice).HasColumnName("unitprice").HasPrecision(18, 2);
                b.Property(x => x.Subtotal).HasColumnName("subtotal").HasPrecision(18, 2);
                b.Property(x => x.CreatedAt).HasColumnName("createdat").HasColumnType("timestamp without time zone");
                b.Property(x => x.UpdatedAt).HasColumnName("updatedat").HasColumnType("timestamp without time zone");
            });

            // =========================
            // DEFECTIVE ITEMS (fixed name + columns)
            // =========================
            modelBuilder.Entity<DefectiveItem>(b =>
            {
                b.ToTable("defectiveitems");
                b.HasKey(x => x.DefectiveItemId);

                b.Property(x => x.DefectiveItemId).HasColumnName("defectiveitemid");
                b.Property(x => x.ProductId).HasColumnName("productid");
                b.Property(x => x.ProductCategoryId).HasColumnName("productcategoryid");
                b.Property(x => x.ReportedDate).HasColumnName("reporteddate");
                b.Property(x => x.DefectDescription).HasColumnName("defectdescription");
                b.Property(x => x.Quantity).HasColumnName("quantity");
                b.Property(x => x.Status).HasColumnName("status");
                b.Property(x => x.CreatedAt).HasColumnName("createdat").HasColumnType("timestamp without time zone");
                b.Property(x => x.UpdatedAt).HasColumnName("updatedat").HasColumnType("timestamp without time zone");
                b.Property(x => x.ReportedByUserId).HasColumnName("reportedbyuserid");

                b.HasOne(x => x.Product)
                    .WithMany(p => p.DefectiveItems)
                    .HasForeignKey(x => x.ProductId);

                b.HasOne<ProductCategory>()
                    .WithMany()
                    .HasForeignKey(x => x.ProductCategoryId);
            });

            // =========================
            // EXPENSE DOMAIN
            // =========================
            // Category
            modelBuilder.Entity<Category>(e =>
            {
                e.ToTable("categories");
                e.HasKey(x => x.Id);

                e.Property(x => x.Id).HasColumnName("id");
                e.Property(x => x.UserId).HasColumnName("user_id");
                e.Property(x => x.Name).HasColumnName("name").IsRequired();
                e.Property(x => x.IsActive).HasColumnName("is_active");
                e.Property(x => x.CreatedAt).HasColumnName("created_at");
                e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            });

            // Contact (add user_id, address, note)
            modelBuilder.Entity<Contact>(e =>
            {
                e.ToTable("contacts");
                e.HasKey(x => x.Id);

                e.Property(x => x.Id).HasColumnName("id");
                e.Property(x => x.UserId).HasColumnName("user_id");
                e.Property(x => x.Name).HasColumnName("name").IsRequired();
                e.Property(x => x.Phone).HasColumnName("phone");
                e.Property(x => x.Email).HasColumnName("email");
                e.Property(x => x.Address).HasColumnName("address");
                e.Property(x => x.Note).HasColumnName("note");
                e.Property(x => x.CreatedAt).HasColumnName("created_at");
                e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            });

            // Label
            modelBuilder.Entity<Label>(e =>
            {
                e.ToTable("labels");
                e.HasKey(x => x.Id);

                e.Property(x => x.Id).HasColumnName("id");
                e.Property(x => x.UserId).HasColumnName("user_id");
                e.Property(x => x.Name).HasColumnName("name").IsRequired();
                e.Property(x => x.Color).HasColumnName("color");
                e.Property(x => x.CreatedAt).HasColumnName("created_at");
            });

            // Expense (add planned_payment_id, tax_json)
            modelBuilder.Entity<Expense>(e =>
            {
                e.ToTable("expenses");
                e.HasKey(x => x.Id);

                e.Property(x => x.Id).HasColumnName("id");
                e.Property(x => x.UserId).HasColumnName("user_id");
                e.Property(x => x.OccurredOn).HasColumnName("occurred_on");
                e.Property(x => x.CategoryId).HasColumnName("category_id");
                e.Property(x => x.Amount).HasColumnName("amount").HasPrecision(18, 2);
                e.Property(x => x.Notes).HasColumnName("notes");
                e.Property(x => x.Status).HasColumnName("status");
                e.Property(x => x.ContactId).HasColumnName("contact_id");
                e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
                e.Property(x => x.CreatedAt).HasColumnName("created_at");
                e.Property(x => x.PlannedPaymentId).HasColumnName("planned_payment_id");
                e.Property(x => x.TaxJson).HasColumnName("tax_json"); // jsonb

                e.HasOne(x => x.CategoryRef)
                    .WithMany()
                    .HasForeignKey(x => x.CategoryId)
                    .HasConstraintName("expenses_category_id_fkey");

                e.HasOne(x => x.ContactRef)
                    .WithMany()
                    .HasForeignKey(x => x.ContactId)
                    .HasConstraintName("expenses_contact_id_fkey");
            });

            // ExpenseLabel
            modelBuilder.Entity<ExpenseLabel>(e =>
            {
                e.ToTable("expense_labels");
                e.HasKey(x => new { x.LabelId, x.ExpenseId });

                e.Property(x => x.ExpenseId).HasColumnName("expense_id");
                e.Property(x => x.LabelId).HasColumnName("label_id");

                e.HasOne(x => x.Expense)
                    .WithMany()
                    .HasForeignKey(x => x.ExpenseId)
                    .HasConstraintName("expense_labels_expense_id_fkey");

                e.HasOne(x => x.Label)
                    .WithMany()
                    .HasForeignKey(x => x.LabelId)
                    .HasConstraintName("expense_labels_label_id_fkey");
            });

            // Budget (table = budget; id column)
            modelBuilder.Entity<Budget>(e =>
            {
                e.ToTable("budget");
                e.HasKey(x => x.BudgetId);

                e.Property(x => x.BudgetId).HasColumnName("id");
                e.Property(x => x.MonthYear).HasColumnName("month_year"); // date
                e.Property(x => x.MonthlyBudgetAmount).HasColumnName("monthly_budget_amount").HasPrecision(18, 2);
                e.Property(x => x.CreatedAt).HasColumnName("created_at");

                e.HasIndex(x => x.MonthYear).IsUnique(false);
            });

            // BudgetHistory (table = budgethistory; id/budget_id)
            modelBuilder.Entity<BudgetHistory>(e =>
            {
                e.ToTable("budgethistory");
                e.HasKey(x => x.BudgetHistoryId);

                e.Property(x => x.BudgetHistoryId).HasColumnName("id");
                e.Property(x => x.BudgetId).HasColumnName("budget_id");
                e.Property(x => x.OldAmount).HasColumnName("old_amount").HasPrecision(18, 2);
                e.Property(x => x.NewAmount).HasColumnName("new_amount").HasPrecision(18, 2);
                e.Property(x => x.CreatedAt).HasColumnName("created_at");

                e.HasOne(x => x.Budget)
                    .WithMany()
                    .HasForeignKey(x => x.BudgetId)
                    .OnDelete(DeleteBehavior.Cascade);

                e.HasIndex(x => x.BudgetId);
            });

            // =========================
            // Attachments
            // =========================
            modelBuilder.Entity<Attachment>(e =>
            {
                e.ToTable("attachments");
                e.HasKey(x => x.Id);

                e.Property(x => x.Id).HasColumnName("id");
                e.Property(x => x.UserId).HasColumnName("user_id");
                e.Property(x => x.ExpenseId).HasColumnName("expense_id");
                e.Property(x => x.StorageKey).HasColumnName("storage_key");
                e.Property(x => x.MimeType).HasColumnName("mime_type");
                e.Property(x => x.SizeBytes).HasColumnName("size_bytes");
                e.Property(x => x.UploadedAt).HasColumnName("uploaded_at");
                e.Property(x => x.CreatedAt).HasColumnName("created_at");
            });

            // =========================
            // Planned Payments
            // =========================
            modelBuilder.Entity<PlannedPayment>(e =>
            {
                e.ToTable("planned_payments");
                e.HasKey(x => x.Id);

                e.Property(x => x.Id).HasColumnName("id");
                e.Property(x => x.UserId).HasColumnName("user_id");
                e.Property(x => x.Name).HasColumnName("name").IsRequired();
                e.Property(x => x.CategoryId).HasColumnName("category_id");
                e.Property(x => x.Amount).HasColumnName("amount").HasPrecision(18, 2);
                e.Property(x => x.ContactId).HasColumnName("contact_id");
                e.Property(x => x.Frequency).HasColumnName("frequency"); // enum in DB → string in code
                e.Property(x => x.DueDate).HasColumnName("due_date");
                e.Property(x => x.Notes).HasColumnName("notes");
                e.Property(x => x.LabelId).HasColumnName("label_id");
                e.Property(x => x.Notify).HasColumnName("notify");       // enum in DB → string in code
                e.Property(x => x.CreatedAt).HasColumnName("created_at");
                e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
                e.Property(x => x.ExpenseId).HasColumnName("expense_id");
                e.Property(x => x.CompletedAt).HasColumnName("completed_at");
            });

            modelBuilder.Entity<PlannedRecurrence>(e =>
            {
                e.ToTable("planned_recurrence");
                e.HasKey(x => x.PlannedPaymentId);

                e.Property(x => x.PlannedPaymentId).HasColumnName("planned_payment_id");
                e.Property(x => x.Repeat).HasColumnName("repeat");   // enum → string
                e.Property(x => x.Every).HasColumnName("every");
                e.Property(x => x.Duration).HasColumnName("duration"); // enum → string
                e.Property(x => x.UntilDate).HasColumnName("until_date");
                e.Property(x => x.OccurrencesCount).HasColumnName("occurrences_count");

                e.HasOne<PlannedPayment>()
                    .WithOne()
                    .HasForeignKey<PlannedRecurrence>(x => x.PlannedPaymentId);
            });
            // TIP: If you prefer, register UseSnakeCaseNamingConvention() on optionsBuilder to reduce HasColumnName boilerplate.
        }
    }
}
