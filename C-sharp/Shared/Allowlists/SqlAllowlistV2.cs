using System;
using System.Collections.Generic;
using Shared.Allowlists;

namespace Shared.Allowlists
{
    public class SqlAllowlistV2 : ISqlAllowlist
    {
        // Backing set of allowed tables (updated to match AppDbContext)
        private static readonly HashSet<string> _tables = new(StringComparer.OrdinalIgnoreCase)
        {
            "attachments",
            "budget",
            "budget_history",
            "categories",
            "contacts",
            "defective_items",
            "expense_labels",
            "expenses",
            "labels",
            "order_items",
            "orders",
            "planned_payments",
            "planned_recurrence",
            "product_category",
            "products",
            "sales",
            "suppliers"
        };

        // ISqlAllowlist.Tables
        public IReadOnlyCollection<string> Tables => _tables;

        public int MaxLimit => 1000;
        public int DefaultLimit => 50;

        private static readonly Dictionary<string, HashSet<string>> Columns =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["attachments"] = new(StringComparer.OrdinalIgnoreCase)
                {
                    "id",
                    "user_id",
                    "expense_id",
                    "storage_key",
                    "mime_type",
                    "size_bytes",
                    "uploaded_at",
                    "created_at"
                },
                ["budget"] = new(StringComparer.OrdinalIgnoreCase)
                {
                    "id",
                    "month_year",
                    "monthly_budget_amount",
                    "created_at"
                },
                ["budget_history"] = new(StringComparer.OrdinalIgnoreCase)
                {
                    "id",
                    "budget_id",
                    "old_amount",
                    "new_amount",
                    "created_at"
                },
                ["categories"] = new(StringComparer.OrdinalIgnoreCase)
                {
                    "id",
                    "user_id",
                    "name",
                    "parent_id",
                    "sort_order",
                    "is_active",
                    "created_at",
                    "updated_at"
                },
                ["contacts"] = new(StringComparer.OrdinalIgnoreCase)
                {
                    "id",
                    "user_id",
                    "name",
                    "phone",
                    "email",
                    "address",
                    "note",
                    "created_at",
                    "updated_at"
                },
                ["defective_items"] = new(StringComparer.OrdinalIgnoreCase)
                {
                    "defective_item_id",
                    "product_id",
                    "product_category_id",
                    "reported_date",
                    "defect_description",
                    "quantity",
                    "status",
                    "created_at",
                    "updated_at",
                    "reported_by_user_id"
                },
                ["expense_labels"] = new(StringComparer.OrdinalIgnoreCase)
                {
                    "expense_id",
                    "label_id"
                },
                ["expenses"] = new(StringComparer.OrdinalIgnoreCase)
                {
                    "id",
                    "user_id",
                    "occurred_on",
                    "category_id",
                    "amount",
                    "notes",
                    "status",
                    "contact_id",
                    "updated_at",
                    "created_at",
                    "planned_payment_id",
                    "tax_json"
                },
                ["labels"] = new(StringComparer.OrdinalIgnoreCase)
                {
                    "id",
                    "user_id",
                    "name",
                    "color",
                    "created_at"
                },
                ["order_items"] = new(StringComparer.OrdinalIgnoreCase)
                {
                    "order_item_id",
                    "order_id",
                    "product_id",
                    "product_category_id",
                    "quantity",
                    "unit_price",
                    "subtotal",
                    "created_at",
                    "updated_at"
                },
                ["orders"] = new(StringComparer.OrdinalIgnoreCase)
                {
                    "orderid",  // actual column name (no underscore)
                    "order_id",  // legacy alias
                    "orderdate",  // actual column name
                    "order_date",  // legacy alias
                    "totalamount",  // actual column name (no underscore)
                    "total_amount",  // legacy alias (with underscore)
                    "orderstatus",  // actual column name
                    "order_status",  // legacy alias
                    "createdat",  // actual column name
                    "created_at",  // legacy alias
                    "updatedat",  // actual column name
                    "updated_at",  // legacy alias
                    "amount_paid",  // actual column name (with underscore)
                    "amountpaid",  // legacy alias
                    "change"
                },
                ["planned_payments"] = new(StringComparer.OrdinalIgnoreCase)
                {
                    "id",
                    "user_id",
                    "name",
                    "category_id",
                    "amount",
                    "contact_id",
                    "frequency",
                    "due_date",
                    "notes",
                    "label_id",
                    "notify",
                    "created_at",
                    "updated_at",
                    "expense_id",
                    "completed_at",
                    "tax_json"
                },
                ["planned_recurrence"] = new(StringComparer.OrdinalIgnoreCase)
                {
                    "planned_payment_id",
                    "repeat",
                    "every",
                    "duration",
                    "until_date",
                    "occurrences_count"
                },
                ["product_category"] = new(StringComparer.OrdinalIgnoreCase)
                {
                    "product_category_id",
                    "product_id",
                    "price",
                    "cost",
                    "color",
                    "age_size",
                    "current_stock",
                    "reorder_point",
                    "updated_stock"
                },
                ["products"] = new(StringComparer.OrdinalIgnoreCase)
                {
                    "productid",  // actual column name (no underscore)
                    "product_id",  // legacy alias
                    "productname",  // actual column name
                    "product_name",  // legacy alias
                    "description",
                    "supplierid",  // actual column name
                    "supplier_id",  // legacy alias
                    "createdat",  // actual column name
                    "created_at",  // legacy alias
                    "updatedat",  // actual column name
                    "updated_at",  // legacy alias
                    "image_url",  // actual column name (with underscore)
                    "imageurl",  // legacy alias
                    "updatedbyuserid",  // actual column name
                    "updated_by_user_id"  // legacy alias
                },
                ["sales"] = new(StringComparer.OrdinalIgnoreCase)
                {
                    "order_id",
                    "order_date",
                    "product_id",
                    "product_name",
                    "quantity",
                    "unit_price",
                    "subtotal",
                    "revenue",
                    "profit",
                    "total_amount",
                    "order_status",
                    "amount_paid",
                    "change",
                    "supplier_id",
                    "supplier_name",
                    "category_id",
                    "product_category_id"
                },
                ["suppliers"] = new(StringComparer.OrdinalIgnoreCase)
                {
                    "supplier_id",
                    "supplier_name",
                    "contact_person",
                    "phone_number",
                    "supplier_email",
                    "address",
                    "created_at",
                    "updated_at",
                    "supplier_status",
                    "defect_returned"
                }
            };

        private static readonly HashSet<string> Operators = new(StringComparer.OrdinalIgnoreCase)
        { "=", "<", ">", "<=", ">=", "LIKE", "ILIKE" };

        public bool IsTableAllowed(string t) => _tables.Contains(t);

        public bool IsColumnAllowed(string t, string c) =>
            Columns.TryGetValue(t, out var set) && set.Contains(c);

        public bool IsOperatorAllowed(string op) => Operators.Contains(op);
    }
}
