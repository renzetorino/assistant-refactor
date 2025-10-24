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
            // Inventory/Sales
            "products", "suppliers", "productcategory", "orders", "orderitems", "defectiveitems",
            // Expense domain
            "expenses", "categories", "contacts", "labels", "expense_labels", "budget", "budgethistory",
            // Attachments, Planner
            "attachments", "planned_payments", "planned_recurrence",
            // Sales reporting view
            "sales"
        };

        // ISqlAllowlist.Tables
        public IReadOnlyCollection<string> Tables => _tables;

        public int MaxLimit => 1000;
        public int DefaultLimit => 50;

        private static readonly Dictionary<string, HashSet<string>> Columns =
            new(StringComparer.OrdinalIgnoreCase)
            {
                // products
                ["products"] = new(StringComparer.OrdinalIgnoreCase)
                { "productid","productname","description","supplierid","createdat","updatedat","image_url","updatedbyuserid" },
                // suppliers
                ["suppliers"] = new(StringComparer.OrdinalIgnoreCase)
                { "supplierid","suppliername","contactperson","phonenumber","supplieremail","address","createdat","updatedat","supplierstatus","defectreturned" },
                // productcategory
                ["productcategory"] = new(StringComparer.OrdinalIgnoreCase)
                { "productcategoryid","productid","price","cost","color","agesize","currentstock","reorderpoint","updatedstock" },
                // orders
                ["orders"] = new(StringComparer.OrdinalIgnoreCase)
                { "orderid","orderdate","totalamount","orderstatus","createdat","updatedat","amount_paid","change" },
                // orderitems
                ["orderitems"] = new(StringComparer.OrdinalIgnoreCase)
                { "orderitemid","orderid","productid","productcategoryid","quantity","unitprice","subtotal","createdat","updatedat" },
                // defectiveitems
                ["defectiveitems"] = new(StringComparer.OrdinalIgnoreCase)
                { "defectiveitemid","productid","productcategoryid","reporteddate","defectdescription","quantity","status","createdat","updatedat","reportedbyuserid" },
                // expenses
                ["expenses"] = new(StringComparer.OrdinalIgnoreCase)
                { "id","user_id","occurred_on","category_id","amount","notes","status","contact_id","updated_at","created_at","planned_payment_id","tax_json" },
                // categories
                ["categories"] = new(StringComparer.OrdinalIgnoreCase)
                { "id","user_id","name","is_active","created_at","updated_at" },
                // contacts
                ["contacts"] = new(StringComparer.OrdinalIgnoreCase)
                { "id","user_id","name","phone","email","address","note","created_at","updated_at" },
                // labels
                ["labels"] = new(StringComparer.OrdinalIgnoreCase)
                { "id","user_id","name","color","created_at" },
                // expense_labels
                ["expense_labels"] = new(StringComparer.OrdinalIgnoreCase)
                { "expense_id","label_id" },
                // budget (table = budget)
                ["budget"] = new(StringComparer.OrdinalIgnoreCase)
                { "id","month_year","monthly_budget_amount","created_at" },
                // budgethistory
                ["budgethistory"] = new(StringComparer.OrdinalIgnoreCase)
                { "id","budget_id","old_amount","new_amount","created_at" },
                // attachments
                ["attachments"] = new(StringComparer.OrdinalIgnoreCase)
                { "id","user_id","expense_id","storage_key","mime_type","size_bytes","uploaded_at","created_at" },
                // planned_payments
                ["planned_payments"] = new(StringComparer.OrdinalIgnoreCase)
                { "id","user_id","name","category_id","amount","contact_id","frequency","due_date","notes","label_id","notify","created_at","updated_at","expense_id","completed_at" },
                // planned_recurrence
                ["planned_recurrence"] = new(StringComparer.OrdinalIgnoreCase)
                { "planned_payment_id","repeat","every","duration","until_date","occurrences_count" },
                // sales (view)
                ["sales"] = new(StringComparer.OrdinalIgnoreCase)
                {
                    // Sales view columns (based on Sales entity and AppDbContext view definition)
                    "orderid", "orderdate", "productid", "productname", 
                    "quantity", "unitprice", "subtotal", "revenue", "profit",
                    // Additional columns that may be useful in the view
                    "totalamount", "orderstatus", "amount_paid", "change",
                    "supplierid", "suppliername", "categoryid", "productcategoryid"
                },
            };

        private static readonly HashSet<string> Operators = new(StringComparer.OrdinalIgnoreCase)
        { "=", "<", ">", "<=", ">=", "LIKE", "ILIKE" };

        public bool IsTableAllowed(string t) => _tables.Contains(t);

        public bool IsColumnAllowed(string t, string c) =>
            Columns.TryGetValue(t, out var set) && set.Contains(c);

        public bool IsOperatorAllowed(string op) => Operators.Contains(op);
    }
}
