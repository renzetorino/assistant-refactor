using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace dataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddChatSystemEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_budgethistory_budgets_budgetid",
                schema: "public",
                table: "budgethistory");

            migrationBuilder.DropForeignKey(
                name: "FK_defective_items_products_ProductId",
                schema: "public",
                table: "defective_items");

            migrationBuilder.DropForeignKey(
                name: "FK_expense_labels_expenses_expenseid",
                schema: "public",
                table: "expense_labels");

            migrationBuilder.DropForeignKey(
                name: "FK_expense_labels_labels_labelid",
                schema: "public",
                table: "expense_labels");

            migrationBuilder.DropForeignKey(
                name: "FK_expenses_categories_categoryid",
                schema: "public",
                table: "expenses");

            migrationBuilder.DropForeignKey(
                name: "FK_order_items_orders_OrderId",
                schema: "public",
                table: "order_items");

            migrationBuilder.DropForeignKey(
                name: "FK_order_items_products_ProductId",
                schema: "public",
                table: "order_items");

            migrationBuilder.DropPrimaryKey(
                name: "PK_suppliers",
                schema: "public",
                table: "suppliers");

            migrationBuilder.DropPrimaryKey(
                name: "PK_products",
                schema: "public",
                table: "products");

            migrationBuilder.DropPrimaryKey(
                name: "PK_productcategory",
                schema: "public",
                table: "productcategory");

            migrationBuilder.DropPrimaryKey(
                name: "PK_orders",
                schema: "public",
                table: "orders");

            migrationBuilder.DropPrimaryKey(
                name: "PK_labels",
                schema: "public",
                table: "labels");

            migrationBuilder.DropIndex(
                name: "IX_labels_userid",
                schema: "public",
                table: "labels");

            migrationBuilder.DropIndex(
                name: "IX_labels_userid_name",
                schema: "public",
                table: "labels");

            migrationBuilder.DropPrimaryKey(
                name: "PK_expenses",
                schema: "public",
                table: "expenses");

            migrationBuilder.DropIndex(
                name: "IX_expenses_createdbyuserid",
                schema: "public",
                table: "expenses");

            migrationBuilder.DropIndex(
                name: "IX_expenses_expensedate",
                schema: "public",
                table: "expenses");

            migrationBuilder.DropPrimaryKey(
                name: "PK_expense_labels",
                schema: "public",
                table: "expense_labels");

            migrationBuilder.DropIndex(
                name: "IX_expense_labels_labelid",
                schema: "public",
                table: "expense_labels");

            migrationBuilder.DropPrimaryKey(
                name: "PK_categories",
                schema: "public",
                table: "categories");

            migrationBuilder.DropIndex(
                name: "IX_categories_userid",
                schema: "public",
                table: "categories");

            migrationBuilder.DropIndex(
                name: "IX_categories_userid_name",
                schema: "public",
                table: "categories");

            migrationBuilder.DropPrimaryKey(
                name: "PK_budgethistory",
                schema: "public",
                table: "budgethistory");

            migrationBuilder.DropPrimaryKey(
                name: "PK_order_items",
                schema: "public",
                table: "order_items");

            migrationBuilder.DropPrimaryKey(
                name: "PK_defective_items",
                schema: "public",
                table: "defective_items");

            migrationBuilder.DropPrimaryKey(
                name: "PK_budgets",
                schema: "public",
                table: "budgets");

            migrationBuilder.DropColumn(
                name: "labelid",
                schema: "public",
                table: "labels");

            migrationBuilder.DropColumn(
                name: "expenseid",
                schema: "public",
                table: "expenses");

            migrationBuilder.DropColumn(
                name: "category",
                schema: "public",
                table: "expenses");

            migrationBuilder.DropColumn(
                name: "description",
                schema: "public",
                table: "expenses");

            migrationBuilder.DropColumn(
                name: "expensedate",
                schema: "public",
                table: "expenses");

            migrationBuilder.DropColumn(
                name: "categoryid",
                schema: "public",
                table: "categories");

            migrationBuilder.RenameTable(
                name: "order_items",
                schema: "public",
                newName: "orderitems",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "defective_items",
                schema: "public",
                newName: "defectiveitems",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "budgets",
                schema: "public",
                newName: "budget",
                newSchema: "public");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                schema: "public",
                table: "orders",
                newName: "updatedat");

            migrationBuilder.RenameColumn(
                name: "TotalAmount",
                schema: "public",
                table: "orders",
                newName: "totalamount");

            migrationBuilder.RenameColumn(
                name: "OrderStatus",
                schema: "public",
                table: "orders",
                newName: "orderstatus");

            migrationBuilder.RenameColumn(
                name: "OrderDate",
                schema: "public",
                table: "orders",
                newName: "orderdate");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                schema: "public",
                table: "orders",
                newName: "createdat");

            migrationBuilder.RenameColumn(
                name: "OrderId",
                schema: "public",
                table: "orders",
                newName: "orderid");

            migrationBuilder.RenameColumn(
                name: "userid",
                schema: "public",
                table: "labels",
                newName: "user_id");

            migrationBuilder.RenameColumn(
                name: "createdat",
                schema: "public",
                table: "labels",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "updatedat",
                schema: "public",
                table: "expenses",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "createdat",
                schema: "public",
                table: "expenses",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "categoryid",
                schema: "public",
                table: "expenses",
                newName: "category_id");

            migrationBuilder.RenameColumn(
                name: "createdbyuserid",
                schema: "public",
                table: "expenses",
                newName: "tax_json");

            migrationBuilder.RenameIndex(
                name: "IX_expenses_categoryid",
                schema: "public",
                table: "expenses",
                newName: "ix_expenses_category_id");

            migrationBuilder.RenameColumn(
                name: "labelid",
                schema: "public",
                table: "expense_labels",
                newName: "label_id");

            migrationBuilder.RenameColumn(
                name: "expenseid",
                schema: "public",
                table: "expense_labels",
                newName: "expense_id");

            migrationBuilder.RenameColumn(
                name: "userid",
                schema: "public",
                table: "categories",
                newName: "user_id");

            migrationBuilder.RenameColumn(
                name: "updatedat",
                schema: "public",
                table: "categories",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "createdat",
                schema: "public",
                table: "categories",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "createdat",
                schema: "public",
                table: "budgethistory",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "budgetid",
                schema: "public",
                table: "budgethistory",
                newName: "budget_id");

            migrationBuilder.RenameColumn(
                name: "budgethistoryid",
                schema: "public",
                table: "budgethistory",
                newName: "id");

            migrationBuilder.RenameIndex(
                name: "IX_budgethistory_budgetid",
                schema: "public",
                table: "budgethistory",
                newName: "ix_budgethistory_budget_id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                schema: "public",
                table: "orderitems",
                newName: "updatedat");

            migrationBuilder.RenameColumn(
                name: "UnitPrice",
                schema: "public",
                table: "orderitems",
                newName: "unitprice");

            migrationBuilder.RenameColumn(
                name: "Subtotal",
                schema: "public",
                table: "orderitems",
                newName: "subtotal");

            migrationBuilder.RenameColumn(
                name: "Quantity",
                schema: "public",
                table: "orderitems",
                newName: "quantity");

            migrationBuilder.RenameColumn(
                name: "ProductId",
                schema: "public",
                table: "orderitems",
                newName: "productid");

            migrationBuilder.RenameColumn(
                name: "OrderId",
                schema: "public",
                table: "orderitems",
                newName: "orderid");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                schema: "public",
                table: "orderitems",
                newName: "createdat");

            migrationBuilder.RenameColumn(
                name: "OrderItemId",
                schema: "public",
                table: "orderitems",
                newName: "orderitemid");

            migrationBuilder.RenameIndex(
                name: "IX_order_items_ProductId",
                schema: "public",
                table: "orderitems",
                newName: "ix_orderitems_product_id");

            migrationBuilder.RenameIndex(
                name: "IX_order_items_OrderId",
                schema: "public",
                table: "orderitems",
                newName: "ix_orderitems_order_id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                schema: "public",
                table: "defectiveitems",
                newName: "updatedat");

            migrationBuilder.RenameColumn(
                name: "Status",
                schema: "public",
                table: "defectiveitems",
                newName: "status");

            migrationBuilder.RenameColumn(
                name: "ReportedDate",
                schema: "public",
                table: "defectiveitems",
                newName: "reporteddate");

            migrationBuilder.RenameColumn(
                name: "ReportedByUserId",
                schema: "public",
                table: "defectiveitems",
                newName: "reportedbyuserid");

            migrationBuilder.RenameColumn(
                name: "Quantity",
                schema: "public",
                table: "defectiveitems",
                newName: "quantity");

            migrationBuilder.RenameColumn(
                name: "ProductId",
                schema: "public",
                table: "defectiveitems",
                newName: "productid");

            migrationBuilder.RenameColumn(
                name: "DefectDescription",
                schema: "public",
                table: "defectiveitems",
                newName: "defectdescription");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                schema: "public",
                table: "defectiveitems",
                newName: "createdat");

            migrationBuilder.RenameColumn(
                name: "DefectiveItemId",
                schema: "public",
                table: "defectiveitems",
                newName: "defectiveitemid");

            migrationBuilder.RenameIndex(
                name: "IX_defective_items_ProductId",
                schema: "public",
                table: "defectiveitems",
                newName: "ix_defectiveitems_product_id");

            migrationBuilder.RenameColumn(
                name: "createdat",
                schema: "public",
                table: "budget",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "budgetid",
                schema: "public",
                table: "budget",
                newName: "id");

            migrationBuilder.RenameIndex(
                name: "IX_budgets_month_year",
                schema: "public",
                table: "budget",
                newName: "ix_budget_month_year");

            migrationBuilder.AlterColumn<Guid>(
                name: "updatedbyuserid",
                schema: "public",
                table: "products",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "updatedat",
                schema: "public",
                table: "orders",
                type: "timestamp without time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "orderstatus",
                schema: "public",
                table: "orders",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<DateTime>(
                name: "orderdate",
                schema: "public",
                table: "orders",
                type: "timestamp without time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "createdat",
                schema: "public",
                table: "orders",
                type: "timestamp without time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AddColumn<decimal>(
                name: "amount_paid",
                schema: "public",
                table: "orders",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "change",
                schema: "public",
                table: "orders",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                schema: "public",
                table: "labels",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120);

            migrationBuilder.AlterColumn<string>(
                name: "color",
                schema: "public",
                table: "labels",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "user_id",
                schema: "public",
                table: "labels",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "id",
                schema: "public",
                table: "labels",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<DateTime>(
                name: "updated_at",
                schema: "public",
                table: "expenses",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<Guid>(
                name: "category_id",
                schema: "public",
                table: "expenses",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "id",
                schema: "public",
                table: "expenses",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "contact_id",
                schema: "public",
                table: "expenses",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "notes",
                schema: "public",
                table: "expenses",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "occurred_on",
                schema: "public",
                table: "expenses",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<Guid>(
                name: "planned_payment_id",
                schema: "public",
                table: "expenses",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                schema: "public",
                table: "expenses",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "user_id",
                schema: "public",
                table: "expenses",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "label_id",
                schema: "public",
                table: "expense_labels",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<Guid>(
                name: "expense_id",
                schema: "public",
                table: "expense_labels",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "name",
                schema: "public",
                table: "categories",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120);

            migrationBuilder.AlterColumn<bool>(
                name: "is_active",
                schema: "public",
                table: "categories",
                type: "boolean",
                nullable: true,
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<Guid>(
                name: "user_id",
                schema: "public",
                table: "categories",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "updated_at",
                schema: "public",
                table: "categories",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at",
                schema: "public",
                table: "categories",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AddColumn<Guid>(
                name: "id",
                schema: "public",
                table: "categories",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<decimal>(
                name: "old_amount",
                schema: "public",
                table: "budgethistory",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<decimal>(
                name: "new_amount",
                schema: "public",
                table: "budgethistory",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<int>(
                name: "budget_id",
                schema: "public",
                table: "budgethistory",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<long>(
                name: "id",
                schema: "public",
                table: "budgethistory",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer")
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn)
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AlterColumn<DateTime>(
                name: "updatedat",
                schema: "public",
                table: "orderitems",
                type: "timestamp without time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "createdat",
                schema: "public",
                table: "orderitems",
                type: "timestamp without time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AddColumn<int>(
                name: "productcategoryid",
                schema: "public",
                table: "orderitems",
                type: "integer",
                nullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "updatedat",
                schema: "public",
                table: "defectiveitems",
                type: "timestamp without time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                schema: "public",
                table: "defectiveitems",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<DateOnly>(
                name: "reporteddate",
                schema: "public",
                table: "defectiveitems",
                type: "date",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<Guid>(
                name: "reportedbyuserid",
                schema: "public",
                table: "defectiveitems",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "defectdescription",
                schema: "public",
                table: "defectiveitems",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(250)",
                oldMaxLength: 250);

            migrationBuilder.AlterColumn<DateTime>(
                name: "createdat",
                schema: "public",
                table: "defectiveitems",
                type: "timestamp without time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AddColumn<int>(
                name: "productcategoryid",
                schema: "public",
                table: "defectiveitems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<decimal>(
                name: "monthly_budget_amount",
                schema: "public",
                table: "budget",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AddPrimaryKey(
                name: "pk_suppliers",
                schema: "public",
                table: "suppliers",
                column: "supplierid");

            migrationBuilder.AddPrimaryKey(
                name: "pk_products",
                schema: "public",
                table: "products",
                column: "productid");

            migrationBuilder.AddPrimaryKey(
                name: "pk_productcategory",
                schema: "public",
                table: "productcategory",
                column: "productcategoryid");

            migrationBuilder.AddPrimaryKey(
                name: "pk_orders",
                schema: "public",
                table: "orders",
                column: "orderid");

            migrationBuilder.AddPrimaryKey(
                name: "pk_labels",
                schema: "public",
                table: "labels",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_expenses",
                schema: "public",
                table: "expenses",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_expense_labels",
                schema: "public",
                table: "expense_labels",
                columns: new[] { "label_id", "expense_id" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_categories",
                schema: "public",
                table: "categories",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_budgethistory",
                schema: "public",
                table: "budgethistory",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_orderitems",
                schema: "public",
                table: "orderitems",
                column: "orderitemid");

            migrationBuilder.AddPrimaryKey(
                name: "pk_defectiveitems",
                schema: "public",
                table: "defectiveitems",
                column: "defectiveitemid");

            migrationBuilder.AddPrimaryKey(
                name: "pk_budget",
                schema: "public",
                table: "budget",
                column: "id");

            migrationBuilder.CreateTable(
                name: "attachments",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    expense_id = table.Column<Guid>(type: "uuid", nullable: true),
                    storage_key = table.Column<string>(type: "text", nullable: true),
                    mime_type = table.Column<string>(type: "text", nullable: true),
                    size_bytes = table.Column<int>(type: "integer", nullable: true),
                    uploaded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attachments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "chat_sessions",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_activity_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    metadata = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_sessions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "contacts",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "text", nullable: false),
                    phone = table.Column<string>(type: "text", nullable: true),
                    email = table.Column<string>(type: "text", nullable: true),
                    address = table.Column<string>(type: "text", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contacts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "faq_search_logs",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    query = table.Column<string>(type: "text", nullable: false),
                    intent = table.Column<string>(type: "text", nullable: false),
                    answer_snippet = table.Column<string>(type: "text", nullable: true),
                    confidence = table.Column<decimal>(type: "numeric", nullable: false),
                    helpful = table.Column<bool>(type: "boolean", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_faq_search_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "planned_payments",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    frequency = table.Column<string>(type: "text", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    label_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notify = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expense_id = table.Column<Guid>(type: "uuid", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_planned_payments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "chat_messages",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    intent = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    domain = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    confidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                    sql_generated = table.Column<string>(type: "text", nullable: true),
                    sql_validated = table.Column<bool>(type: "boolean", nullable: true),
                    sql_executed = table.Column<bool>(type: "boolean", nullable: true),
                    result_count = table.Column<int>(type: "integer", nullable: true),
                    latency_ms = table.Column<int>(type: "integer", nullable: true),
                    model_used = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_chat_messages_chat_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "public",
                        principalTable: "chat_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "planned_recurrence",
                schema: "public",
                columns: table => new
                {
                    planned_payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    repeat = table.Column<string>(type: "text", nullable: false),
                    every = table.Column<int>(type: "integer", nullable: false),
                    duration = table.Column<string>(type: "text", nullable: false),
                    until_date = table.Column<DateOnly>(type: "date", nullable: true),
                    occurrences_count = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_planned_recurrence", x => x.planned_payment_id);
                    table.ForeignKey(
                        name: "fk_planned_recurrence_planned_payments_planned_payment_id",
                        column: x => x.planned_payment_id,
                        principalSchema: "public",
                        principalTable: "planned_payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chat_feedback",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    feedback_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    rating = table.Column<int>(type: "integer", nullable: true),
                    comment = table.Column<string>(type: "text", nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_feedback", x => x.id);
                    table.ForeignKey(
                        name: "fk_chat_feedback_chat_messages_message_id",
                        column: x => x.message_id,
                        principalSchema: "public",
                        principalTable: "chat_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_chat_feedback_chat_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "public",
                        principalTable: "chat_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_expenses_contact_id",
                schema: "public",
                table: "expenses",
                column: "contact_id");

            migrationBuilder.CreateIndex(
                name: "ix_expense_labels_expense_id",
                schema: "public",
                table: "expense_labels",
                column: "expense_id");

            migrationBuilder.CreateIndex(
                name: "ix_defectiveitems_productcategoryid",
                schema: "public",
                table: "defectiveitems",
                column: "productcategoryid");

            migrationBuilder.CreateIndex(
                name: "idx_chat_feedback_feedback_type",
                schema: "public",
                table: "chat_feedback",
                column: "feedback_type");

            migrationBuilder.CreateIndex(
                name: "idx_chat_feedback_message_id",
                schema: "public",
                table: "chat_feedback",
                column: "message_id");

            migrationBuilder.CreateIndex(
                name: "idx_chat_feedback_session_id",
                schema: "public",
                table: "chat_feedback",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "idx_chat_feedback_user_id",
                schema: "public",
                table: "chat_feedback",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "idx_chat_messages_created_at",
                schema: "public",
                table: "chat_messages",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "idx_chat_messages_intent",
                schema: "public",
                table: "chat_messages",
                column: "intent");

            migrationBuilder.CreateIndex(
                name: "idx_chat_messages_session_id",
                schema: "public",
                table: "chat_messages",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "idx_chat_sessions_expires_at",
                schema: "public",
                table: "chat_sessions",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "idx_chat_sessions_user_id",
                schema: "public",
                table: "chat_sessions",
                column: "user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_budgethistory_budget_budget_id",
                schema: "public",
                table: "budgethistory",
                column: "budget_id",
                principalSchema: "public",
                principalTable: "budget",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_defectiveitems_productcategory_productcategoryid",
                schema: "public",
                table: "defectiveitems",
                column: "productcategoryid",
                principalSchema: "public",
                principalTable: "productcategory",
                principalColumn: "productcategoryid",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_defectiveitems_products_product_id",
                schema: "public",
                table: "defectiveitems",
                column: "productid",
                principalSchema: "public",
                principalTable: "products",
                principalColumn: "productid",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "expense_labels_expense_id_fkey",
                schema: "public",
                table: "expense_labels",
                column: "expense_id",
                principalSchema: "public",
                principalTable: "expenses",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "expense_labels_label_id_fkey",
                schema: "public",
                table: "expense_labels",
                column: "label_id",
                principalSchema: "public",
                principalTable: "labels",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "expenses_category_id_fkey",
                schema: "public",
                table: "expenses",
                column: "category_id",
                principalSchema: "public",
                principalTable: "categories",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "expenses_contact_id_fkey",
                schema: "public",
                table: "expenses",
                column: "contact_id",
                principalSchema: "public",
                principalTable: "contacts",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_orderitems_orders_order_id",
                schema: "public",
                table: "orderitems",
                column: "orderid",
                principalSchema: "public",
                principalTable: "orders",
                principalColumn: "orderid",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_orderitems_products_product_id",
                schema: "public",
                table: "orderitems",
                column: "productid",
                principalSchema: "public",
                principalTable: "products",
                principalColumn: "productid",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_budgethistory_budget_budget_id",
                schema: "public",
                table: "budgethistory");

            migrationBuilder.DropForeignKey(
                name: "fk_defectiveitems_productcategory_productcategoryid",
                schema: "public",
                table: "defectiveitems");

            migrationBuilder.DropForeignKey(
                name: "fk_defectiveitems_products_product_id",
                schema: "public",
                table: "defectiveitems");

            migrationBuilder.DropForeignKey(
                name: "expense_labels_expense_id_fkey",
                schema: "public",
                table: "expense_labels");

            migrationBuilder.DropForeignKey(
                name: "expense_labels_label_id_fkey",
                schema: "public",
                table: "expense_labels");

            migrationBuilder.DropForeignKey(
                name: "expenses_category_id_fkey",
                schema: "public",
                table: "expenses");

            migrationBuilder.DropForeignKey(
                name: "expenses_contact_id_fkey",
                schema: "public",
                table: "expenses");

            migrationBuilder.DropForeignKey(
                name: "fk_orderitems_orders_order_id",
                schema: "public",
                table: "orderitems");

            migrationBuilder.DropForeignKey(
                name: "fk_orderitems_products_product_id",
                schema: "public",
                table: "orderitems");

            migrationBuilder.DropTable(
                name: "attachments",
                schema: "public");

            migrationBuilder.DropTable(
                name: "chat_feedback",
                schema: "public");

            migrationBuilder.DropTable(
                name: "contacts",
                schema: "public");

            migrationBuilder.DropTable(
                name: "faq_search_logs",
                schema: "public");

            migrationBuilder.DropTable(
                name: "planned_recurrence",
                schema: "public");

            migrationBuilder.DropTable(
                name: "chat_messages",
                schema: "public");

            migrationBuilder.DropTable(
                name: "planned_payments",
                schema: "public");

            migrationBuilder.DropTable(
                name: "chat_sessions",
                schema: "public");

            migrationBuilder.DropPrimaryKey(
                name: "pk_suppliers",
                schema: "public",
                table: "suppliers");

            migrationBuilder.DropPrimaryKey(
                name: "pk_products",
                schema: "public",
                table: "products");

            migrationBuilder.DropPrimaryKey(
                name: "pk_productcategory",
                schema: "public",
                table: "productcategory");

            migrationBuilder.DropPrimaryKey(
                name: "pk_orders",
                schema: "public",
                table: "orders");

            migrationBuilder.DropPrimaryKey(
                name: "pk_labels",
                schema: "public",
                table: "labels");

            migrationBuilder.DropPrimaryKey(
                name: "pk_expenses",
                schema: "public",
                table: "expenses");

            migrationBuilder.DropIndex(
                name: "ix_expenses_contact_id",
                schema: "public",
                table: "expenses");

            migrationBuilder.DropPrimaryKey(
                name: "pk_expense_labels",
                schema: "public",
                table: "expense_labels");

            migrationBuilder.DropIndex(
                name: "ix_expense_labels_expense_id",
                schema: "public",
                table: "expense_labels");

            migrationBuilder.DropPrimaryKey(
                name: "pk_categories",
                schema: "public",
                table: "categories");

            migrationBuilder.DropPrimaryKey(
                name: "pk_budgethistory",
                schema: "public",
                table: "budgethistory");

            migrationBuilder.DropPrimaryKey(
                name: "pk_orderitems",
                schema: "public",
                table: "orderitems");

            migrationBuilder.DropPrimaryKey(
                name: "pk_defectiveitems",
                schema: "public",
                table: "defectiveitems");

            migrationBuilder.DropIndex(
                name: "ix_defectiveitems_productcategoryid",
                schema: "public",
                table: "defectiveitems");

            migrationBuilder.DropPrimaryKey(
                name: "pk_budget",
                schema: "public",
                table: "budget");

            migrationBuilder.DropColumn(
                name: "amount_paid",
                schema: "public",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "change",
                schema: "public",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "id",
                schema: "public",
                table: "labels");

            migrationBuilder.DropColumn(
                name: "id",
                schema: "public",
                table: "expenses");

            migrationBuilder.DropColumn(
                name: "contact_id",
                schema: "public",
                table: "expenses");

            migrationBuilder.DropColumn(
                name: "notes",
                schema: "public",
                table: "expenses");

            migrationBuilder.DropColumn(
                name: "occurred_on",
                schema: "public",
                table: "expenses");

            migrationBuilder.DropColumn(
                name: "planned_payment_id",
                schema: "public",
                table: "expenses");

            migrationBuilder.DropColumn(
                name: "status",
                schema: "public",
                table: "expenses");

            migrationBuilder.DropColumn(
                name: "user_id",
                schema: "public",
                table: "expenses");

            migrationBuilder.DropColumn(
                name: "id",
                schema: "public",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "productcategoryid",
                schema: "public",
                table: "orderitems");

            migrationBuilder.DropColumn(
                name: "productcategoryid",
                schema: "public",
                table: "defectiveitems");

            migrationBuilder.RenameTable(
                name: "orderitems",
                schema: "public",
                newName: "order_items",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "defectiveitems",
                schema: "public",
                newName: "defective_items",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "budget",
                schema: "public",
                newName: "budgets",
                newSchema: "public");

            migrationBuilder.RenameColumn(
                name: "updatedat",
                schema: "public",
                table: "orders",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "totalamount",
                schema: "public",
                table: "orders",
                newName: "TotalAmount");

            migrationBuilder.RenameColumn(
                name: "orderstatus",
                schema: "public",
                table: "orders",
                newName: "OrderStatus");

            migrationBuilder.RenameColumn(
                name: "orderdate",
                schema: "public",
                table: "orders",
                newName: "OrderDate");

            migrationBuilder.RenameColumn(
                name: "createdat",
                schema: "public",
                table: "orders",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "orderid",
                schema: "public",
                table: "orders",
                newName: "OrderId");

            migrationBuilder.RenameColumn(
                name: "user_id",
                schema: "public",
                table: "labels",
                newName: "userid");

            migrationBuilder.RenameColumn(
                name: "created_at",
                schema: "public",
                table: "labels",
                newName: "createdat");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                schema: "public",
                table: "expenses",
                newName: "updatedat");

            migrationBuilder.RenameColumn(
                name: "created_at",
                schema: "public",
                table: "expenses",
                newName: "createdat");

            migrationBuilder.RenameColumn(
                name: "category_id",
                schema: "public",
                table: "expenses",
                newName: "categoryid");

            migrationBuilder.RenameColumn(
                name: "tax_json",
                schema: "public",
                table: "expenses",
                newName: "createdbyuserid");

            migrationBuilder.RenameIndex(
                name: "ix_expenses_category_id",
                schema: "public",
                table: "expenses",
                newName: "IX_expenses_categoryid");

            migrationBuilder.RenameColumn(
                name: "expense_id",
                schema: "public",
                table: "expense_labels",
                newName: "expenseid");

            migrationBuilder.RenameColumn(
                name: "label_id",
                schema: "public",
                table: "expense_labels",
                newName: "labelid");

            migrationBuilder.RenameColumn(
                name: "user_id",
                schema: "public",
                table: "categories",
                newName: "userid");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                schema: "public",
                table: "categories",
                newName: "updatedat");

            migrationBuilder.RenameColumn(
                name: "created_at",
                schema: "public",
                table: "categories",
                newName: "createdat");

            migrationBuilder.RenameColumn(
                name: "created_at",
                schema: "public",
                table: "budgethistory",
                newName: "createdat");

            migrationBuilder.RenameColumn(
                name: "budget_id",
                schema: "public",
                table: "budgethistory",
                newName: "budgetid");

            migrationBuilder.RenameColumn(
                name: "id",
                schema: "public",
                table: "budgethistory",
                newName: "budgethistoryid");

            migrationBuilder.RenameIndex(
                name: "ix_budgethistory_budget_id",
                schema: "public",
                table: "budgethistory",
                newName: "IX_budgethistory_budgetid");

            migrationBuilder.RenameColumn(
                name: "updatedat",
                schema: "public",
                table: "order_items",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "unitprice",
                schema: "public",
                table: "order_items",
                newName: "UnitPrice");

            migrationBuilder.RenameColumn(
                name: "subtotal",
                schema: "public",
                table: "order_items",
                newName: "Subtotal");

            migrationBuilder.RenameColumn(
                name: "quantity",
                schema: "public",
                table: "order_items",
                newName: "Quantity");

            migrationBuilder.RenameColumn(
                name: "productid",
                schema: "public",
                table: "order_items",
                newName: "ProductId");

            migrationBuilder.RenameColumn(
                name: "orderid",
                schema: "public",
                table: "order_items",
                newName: "OrderId");

            migrationBuilder.RenameColumn(
                name: "createdat",
                schema: "public",
                table: "order_items",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "orderitemid",
                schema: "public",
                table: "order_items",
                newName: "OrderItemId");

            migrationBuilder.RenameIndex(
                name: "ix_orderitems_product_id",
                schema: "public",
                table: "order_items",
                newName: "IX_order_items_ProductId");

            migrationBuilder.RenameIndex(
                name: "ix_orderitems_order_id",
                schema: "public",
                table: "order_items",
                newName: "IX_order_items_OrderId");

            migrationBuilder.RenameColumn(
                name: "updatedat",
                schema: "public",
                table: "defective_items",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "status",
                schema: "public",
                table: "defective_items",
                newName: "Status");

            migrationBuilder.RenameColumn(
                name: "reporteddate",
                schema: "public",
                table: "defective_items",
                newName: "ReportedDate");

            migrationBuilder.RenameColumn(
                name: "reportedbyuserid",
                schema: "public",
                table: "defective_items",
                newName: "ReportedByUserId");

            migrationBuilder.RenameColumn(
                name: "quantity",
                schema: "public",
                table: "defective_items",
                newName: "Quantity");

            migrationBuilder.RenameColumn(
                name: "productid",
                schema: "public",
                table: "defective_items",
                newName: "ProductId");

            migrationBuilder.RenameColumn(
                name: "defectdescription",
                schema: "public",
                table: "defective_items",
                newName: "DefectDescription");

            migrationBuilder.RenameColumn(
                name: "createdat",
                schema: "public",
                table: "defective_items",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "defectiveitemid",
                schema: "public",
                table: "defective_items",
                newName: "DefectiveItemId");

            migrationBuilder.RenameIndex(
                name: "ix_defectiveitems_product_id",
                schema: "public",
                table: "defective_items",
                newName: "IX_defective_items_ProductId");

            migrationBuilder.RenameColumn(
                name: "created_at",
                schema: "public",
                table: "budgets",
                newName: "createdat");

            migrationBuilder.RenameColumn(
                name: "id",
                schema: "public",
                table: "budgets",
                newName: "budgetid");

            migrationBuilder.RenameIndex(
                name: "ix_budget_month_year",
                schema: "public",
                table: "budgets",
                newName: "IX_budgets_month_year");

            migrationBuilder.AlterColumn<int>(
                name: "updatedbyuserid",
                schema: "public",
                table: "products",
                type: "integer",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "UpdatedAt",
                schema: "public",
                table: "orders",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone");

            migrationBuilder.AlterColumn<string>(
                name: "OrderStatus",
                schema: "public",
                table: "orders",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<DateTime>(
                name: "OrderDate",
                schema: "public",
                table: "orders",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                schema: "public",
                table: "orders",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone");

            migrationBuilder.AlterColumn<string>(
                name: "name",
                schema: "public",
                table: "labels",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "color",
                schema: "public",
                table: "labels",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "userid",
                schema: "public",
                table: "labels",
                type: "text",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "labelid",
                schema: "public",
                table: "labels",
                type: "integer",
                nullable: false,
                defaultValue: 0)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AlterColumn<DateTime>(
                name: "updatedat",
                schema: "public",
                table: "expenses",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "categoryid",
                schema: "public",
                table: "expenses",
                type: "integer",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "expenseid",
                schema: "public",
                table: "expenses",
                type: "integer",
                nullable: false,
                defaultValue: 0)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddColumn<string>(
                name: "category",
                schema: "public",
                table: "expenses",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "description",
                schema: "public",
                table: "expenses",
                type: "character varying(250)",
                maxLength: 250,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "expensedate",
                schema: "public",
                table: "expenses",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AlterColumn<int>(
                name: "expenseid",
                schema: "public",
                table: "expense_labels",
                type: "integer",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<int>(
                name: "labelid",
                schema: "public",
                table: "expense_labels",
                type: "integer",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "name",
                schema: "public",
                table: "categories",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<bool>(
                name: "is_active",
                schema: "public",
                table: "categories",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "userid",
                schema: "public",
                table: "categories",
                type: "text",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "updatedat",
                schema: "public",
                table: "categories",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "createdat",
                schema: "public",
                table: "categories",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "categoryid",
                schema: "public",
                table: "categories",
                type: "integer",
                nullable: false,
                defaultValue: 0)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AlterColumn<decimal>(
                name: "old_amount",
                schema: "public",
                table: "budgethistory",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "new_amount",
                schema: "public",
                table: "budgethistory",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "budgetid",
                schema: "public",
                table: "budgethistory",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "budgethistoryid",
                schema: "public",
                table: "budgethistory",
                type: "integer",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint")
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn)
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AlterColumn<DateTime>(
                name: "UpdatedAt",
                schema: "public",
                table: "order_items",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                schema: "public",
                table: "order_items",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "UpdatedAt",
                schema: "public",
                table: "defective_items",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                schema: "public",
                table: "defective_items",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "ReportedDate",
                schema: "public",
                table: "defective_items",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateOnly),
                oldType: "date");

            migrationBuilder.AlterColumn<int>(
                name: "ReportedByUserId",
                schema: "public",
                table: "defective_items",
                type: "integer",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "DefectDescription",
                schema: "public",
                table: "defective_items",
                type: "character varying(250)",
                maxLength: 250,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                schema: "public",
                table: "defective_items",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone");

            migrationBuilder.AlterColumn<decimal>(
                name: "monthly_budget_amount",
                schema: "public",
                table: "budgets",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_suppliers",
                schema: "public",
                table: "suppliers",
                column: "supplierid");

            migrationBuilder.AddPrimaryKey(
                name: "PK_products",
                schema: "public",
                table: "products",
                column: "productid");

            migrationBuilder.AddPrimaryKey(
                name: "PK_productcategory",
                schema: "public",
                table: "productcategory",
                column: "productcategoryid");

            migrationBuilder.AddPrimaryKey(
                name: "PK_orders",
                schema: "public",
                table: "orders",
                column: "OrderId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_labels",
                schema: "public",
                table: "labels",
                column: "labelid");

            migrationBuilder.AddPrimaryKey(
                name: "PK_expenses",
                schema: "public",
                table: "expenses",
                column: "expenseid");

            migrationBuilder.AddPrimaryKey(
                name: "PK_expense_labels",
                schema: "public",
                table: "expense_labels",
                columns: new[] { "expenseid", "labelid" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_categories",
                schema: "public",
                table: "categories",
                column: "categoryid");

            migrationBuilder.AddPrimaryKey(
                name: "PK_budgethistory",
                schema: "public",
                table: "budgethistory",
                column: "budgethistoryid");

            migrationBuilder.AddPrimaryKey(
                name: "PK_order_items",
                schema: "public",
                table: "order_items",
                column: "OrderItemId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_defective_items",
                schema: "public",
                table: "defective_items",
                column: "DefectiveItemId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_budgets",
                schema: "public",
                table: "budgets",
                column: "budgetid");

            migrationBuilder.CreateIndex(
                name: "IX_labels_userid",
                schema: "public",
                table: "labels",
                column: "userid");

            migrationBuilder.CreateIndex(
                name: "IX_labels_userid_name",
                schema: "public",
                table: "labels",
                columns: new[] { "userid", "name" });

            migrationBuilder.CreateIndex(
                name: "IX_expenses_createdbyuserid",
                schema: "public",
                table: "expenses",
                column: "createdbyuserid");

            migrationBuilder.CreateIndex(
                name: "IX_expenses_expensedate",
                schema: "public",
                table: "expenses",
                column: "expensedate");

            migrationBuilder.CreateIndex(
                name: "IX_expense_labels_labelid",
                schema: "public",
                table: "expense_labels",
                column: "labelid");

            migrationBuilder.CreateIndex(
                name: "IX_categories_userid",
                schema: "public",
                table: "categories",
                column: "userid");

            migrationBuilder.CreateIndex(
                name: "IX_categories_userid_name",
                schema: "public",
                table: "categories",
                columns: new[] { "userid", "name" });

            migrationBuilder.AddForeignKey(
                name: "FK_budgethistory_budgets_budgetid",
                schema: "public",
                table: "budgethistory",
                column: "budgetid",
                principalSchema: "public",
                principalTable: "budgets",
                principalColumn: "budgetid",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_defective_items_products_ProductId",
                schema: "public",
                table: "defective_items",
                column: "ProductId",
                principalSchema: "public",
                principalTable: "products",
                principalColumn: "productid",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_expense_labels_expenses_expenseid",
                schema: "public",
                table: "expense_labels",
                column: "expenseid",
                principalSchema: "public",
                principalTable: "expenses",
                principalColumn: "expenseid",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_expense_labels_labels_labelid",
                schema: "public",
                table: "expense_labels",
                column: "labelid",
                principalSchema: "public",
                principalTable: "labels",
                principalColumn: "labelid",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_expenses_categories_categoryid",
                schema: "public",
                table: "expenses",
                column: "categoryid",
                principalSchema: "public",
                principalTable: "categories",
                principalColumn: "categoryid",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_order_items_orders_OrderId",
                schema: "public",
                table: "order_items",
                column: "OrderId",
                principalSchema: "public",
                principalTable: "orders",
                principalColumn: "OrderId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_order_items_products_ProductId",
                schema: "public",
                table: "order_items",
                column: "ProductId",
                principalSchema: "public",
                principalTable: "products",
                principalColumn: "productid",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
