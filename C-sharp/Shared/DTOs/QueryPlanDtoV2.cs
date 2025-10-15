// Shared/DTOs/QueryPlanDtoV2.cs
namespace Shared.DTOs
{
    public record QueryPlanDtoV2
    {
        public string Table { get; init; } = "";            // e.g., "productcategory"
        public List<string>? Columns { get; init; }         // null => "*"
        public List<FilterClause>? Filters { get; init; }   // optional
        public SortSpec? Sort { get; init; }                // optional
        public int? Limit { get; init; }                    // optional
    }

    public record FilterClause
    {
        public string Column { get; init; } = "";           // e.g., "color"
        public string Operator { get; init; } = "";         // e.g., "ILIKE"
        public object? Value { get; init; }                 // e.g., "%blue%"
    }

    public record SortSpec
    {
        public string Column { get; init; } = "";           // e.g., "productcategoryid"
        public string? Direction { get; init; }             // "ASC"/"DESC" (default ASC)
    }
}
