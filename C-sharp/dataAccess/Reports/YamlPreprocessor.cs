using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace dataAccess.Reports;

public sealed class YamlPreprocessor
{
    private readonly ILlmDateParser _dateParser;

    public YamlPreprocessor(ILlmDateParser dateParser) => _dateParser = dateParser;

    public async Task<(bool Allowed, string? Message, Hints Data)> PrepareAsync(string domain, string userText, CancellationToken ct = default)
    {
        var txt = (userText ?? string.Empty).Trim();

        // Inventory comparisons are blocked
        var compare = WantsCompare(txt);
        if (domain == "inventory" && compare)
            return (false, "Inventory is a snapshot (no comparisons). Try 'inventory summary for today.'", Hints.Empty);

        // Time window - use LLM-based date parser
        var (startDate, endDate) = await _dateParser.ParseDateRangeAsync(txt, ct);
        var start = startDate.ToString("yyyy-MM-dd");
        var end = endDate.ToString("yyyy-MM-dd");
        
        // Generate label
        var label = (startDate.Year == endDate.Year)
            ? $"{startDate:MMM d}–{endDate:MMM d, yyyy}"
            : $"{startDate:MMM d, yyyy}–{endDate:MMM d, yyyy}";

        // Calculate prior window of equal length (for comparisons)
        var days = (endDate - startDate).Days + 1;
        var prevEndDate = startDate.AddDays(-1);
        var prevStartDate = prevEndDate.AddDays(-(days - 1));
        var prevStart = compare ? prevStartDate.ToString("yyyy-MM-dd") : null;
        var prevEnd = compare ? prevEndDate.ToString("yyyy-MM-dd") : null;
        
        // Determine preset (simplified - LLM handles complexity)
        var preset = DeterminePreset(txt);

        // Lightweight tag parsing for scope/product/topk
        // Accepted tags (case-insensitive):
        //   [SCOPE=item] | [SCOPE=overall]
        //   [PRODUCT_ID=xyz123]
        //   [TOPK=5]
        string? scope = null;
        string? productId = null;
        int? topK = null;

        var mScope = Regex.Match(txt, @"\[SCOPE\s*=\s*(item|overall)\]", RegexOptions.IgnoreCase);
        if (mScope.Success) scope = mScope.Groups[1].Value.ToLowerInvariant();

        var mPid = Regex.Match(txt, @"\[PRODUCT_ID\s*=\s*([^\]\s]+)\]", RegexOptions.IgnoreCase);
        if (mPid.Success) productId = mPid.Groups[1].Value;

        var mTopk = Regex.Match(txt, @"\[TOPK\s*=\s*(\d{1,3})\]", RegexOptions.IgnoreCase);
        if (mTopk.Success && int.TryParse(mTopk.Groups[1].Value, out var k) && k > 0) topK = k;

        // If product is present but scope missing → assume item
        if (productId is not null && string.IsNullOrWhiteSpace(scope))
            scope = "item";

        // Default scope for sales: overall
        if (domain == "sales" && string.IsNullOrWhiteSpace(scope))
            scope = "overall";

        return (true, null, new Hints
        {
            Start = start,
            End = end,
            Label = label,
            TimePreset = preset,
            CompareToPrior = compare,
            PrevStart = compare ? prevStart : null,
            PrevEnd = compare ? prevEnd : null,
            Filters = null,
            UserId = null,
            Scope = scope,
            ProductId = productId,
            TopK = topK
        });
    }

    private static bool WantsCompare(string userText)
        => Regex.IsMatch((userText ?? string.Empty).ToLowerInvariant(),
           @"\b(compare|vs|versus|kumpara|ihambing|wow|mom|yoy|year over year)\b");

    private static string DeterminePreset(string text)
    {
        var t = text.ToLowerInvariant();
        if (Regex.IsMatch(t, @"\byesterday\b|kahapon")) return "yesterday";
        if (Regex.IsMatch(t, @"\btoday\b|ngayong\s*araw|ngayon")) return "today";
        if (Regex.IsMatch(t, @"\bthis\s+week\b|ngayong\s+linggo")) return "this_week";
        if (Regex.IsMatch(t, @"\blast\s+week\b|nakaraang\s+linggo")) return "last_week";
        if (Regex.IsMatch(t, @"\bthis\s+month\b|ngayong\s+buwan")) return "this_month";
        if (Regex.IsMatch(t, @"\blast\s+month\b|nakaraang\s+buwan")) return "last_month";
        if (Regex.IsMatch(t, @"\blast\s+\d+\s+(days?|araw)\b")) return "last_n_days";
        if (Regex.IsMatch(t, @"\blast\s+\d+\s+(weeks?|linggo)\b")) return "last_n_weeks";
        if (Regex.IsMatch(t, @"\blast\s+\d+\s+(months?|buwan)\b")) return "last_n_months";
        return "custom";
    }

    public sealed class Hints
    {
        public static readonly Hints Empty = new();
        public string? Start { get; init; }
        public string? End { get; init; }
        public string? Label { get; init; }
        public string? TimePreset { get; init; }
        public bool CompareToPrior { get; init; }
        public string? PrevStart { get; init; }
        public string? PrevEnd { get; init; }
        public object? Filters { get; init; }
        public string? UserId { get; init; }

        // NEW: sales scope hints
        public string? Scope { get; init; }          // "overall" | "item"
        public string? ProductId { get; init; }      // required if Scope=item
        public int? TopK { get; init; }              // optional for best-sellers/variants
    }
}
