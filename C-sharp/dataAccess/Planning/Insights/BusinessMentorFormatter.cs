using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using dataAccess.Contracts;
using dataAccess.LLM;
using dataAccess.Reports;
using Microsoft.Extensions.Logging;

namespace dataAccess.Planning.Insights
{
    /// <summary>
    /// Converts raw detector findings into mentor-style coaching copy using Gemini.
    /// </summary>
    public sealed class BusinessMentorFormatter
    {
        private readonly IGeminiJsonClient _geminiClient;
        private readonly ILogger<BusinessMentorFormatter> _logger;

        private const int MAX_RETRIES = 3;
        private const int BASE_DELAY_MS = 1000;

        public BusinessMentorFormatter(IGeminiJsonClient geminiClient, ILogger<BusinessMentorFormatter> logger)
        {
            _geminiClient = geminiClient;
            _logger = logger;
        }

        /// <summary>
        /// Formats all findings from a scan into mentor-style text grouped by category.
        /// </summary>
        public async Task<Dictionary<string, string>> FormatInsightsAsync(
            InsightScanResult scanResult,
            CancellationToken ct = default)
        {
            _logger.LogInformation(
                "[BusinessMentorFormatter] Formatting insights for BusinessId: {BusinessId}, Findings: {Count}",
                scanResult.BusinessId, scanResult.Findings.Count);

            if (!scanResult.Findings.Any())
            {
                _logger.LogInformation("[BusinessMentorFormatter] No findings to format");
                return new Dictionary<string, string>();
            }

            // Group findings by category (inventory, finance, sales)
            var groupedFindings = scanResult.Findings
                .GroupBy(f => f.Category)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Sprint 3: Format all categories in parallel for 80% faster execution
            var formatTasks = groupedFindings.Select(async kvp =>
            {
                try
                {
                    var mentorText = await FormatCategoryWithRetryAsync(kvp.Key, kvp.Value, ct);
                    return (category: kvp.Key, text: mentorText, success: true);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "[BusinessMentorFormatter] Failed to format category: {Category} after all retries",
                        kvp.Key);
                    
                    // Fallback: Use raw findings as plain text
                    var fallbackText = CreateFallbackText(kvp.Key, kvp.Value);
                    return (category: kvp.Key, text: fallbackText, success: false);
                }
            });

            var results = await Task.WhenAll(formatTasks);
            
            var formattedInsights = results.ToDictionary(r => r.category, r => r.text);

            _logger.LogInformation(
                "[BusinessMentorFormatter] Formatted {CategoryCount} categories successfully",
                formattedInsights.Count);

            return formattedInsights;
        }

        /// <summary>
        /// Formats a single category with exponential backoff retry logic.
        /// </summary>
        private async Task<string> FormatCategoryWithRetryAsync(
            string category,
            List<DetectorFinding> findings,
            CancellationToken ct)
        {
            Exception? lastException = null;

            for (int attempt = 0; attempt < MAX_RETRIES; attempt++)
            {
                try
                {
                    return await FormatCategoryAsync(category, findings, ct);
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    
                    if (attempt < MAX_RETRIES - 1)
                    {
                        var delay = BASE_DELAY_MS * (int)Math.Pow(2, attempt);
                        _logger.LogWarning(ex,
                            "[BusinessMentorFormatter] Attempt {Attempt}/{MaxRetries} failed for category {Category}. " +
                            "Retrying in {Delay}ms",
                            attempt + 1, MAX_RETRIES, category, delay);
                        
                        await Task.Delay(delay, ct);
                    }
                }
            }

            throw lastException ?? new Exception($"Failed to format category {category} after {MAX_RETRIES} attempts");
        }

        /// <summary>
        /// Calls Gemini to format findings into mentor-style text.
        /// </summary>
        private async Task<string> FormatCategoryAsync(
            string category,
            List<DetectorFinding> findings,
            CancellationToken ct)
        {
            var systemPrompt = BuildMentorSystemPrompt(category);
            var userPrompt = BuildUserPrompt(findings);

            _logger.LogDebug(
                "[BusinessMentorFormatter] Calling Gemini for category: {Category}, Findings: {Count}",
                category, findings.Count);

            // Call Gemini API
            var response = await _geminiClient.GenerateJsonAsync(
                systemPrompt,
                userPrompt,
                temperature: 0.7, // Higher temperature for creative, warm tone
                ct);

            // Extract mentor text from response
            var mentorText = ExtractMentorText(response);

            _logger.LogDebug(
                "[BusinessMentorFormatter] Gemini response received for category: {Category}, Length: {Length}",
                category, mentorText.Length);

            return mentorText;
        }

        /// <summary>
        /// Builds the system prompt for mentor-style coaching.
        /// </summary>
        private string BuildMentorSystemPrompt(string category)
        {
            var categoryContext = category switch
            {
                "inventory" => "stock management and inventory health",
                "finance" => "budget tracking and expense management",
                "sales" => "pricing strategy and profit margins",
                _ => "business operations"
            };

            return $@"You are a supportive Filipino business mentor helping a small business owner understand their {categoryContext}.

TONE:
- Warm, encouraging, and action-oriented
- Use Taglish (mix of Filipino and English) naturally
- Avoid jargon - explain concepts simply
- Focus on ""what to do next"" not just ""what's wrong""

STRUCTURE:
- Start with acknowledgment (""I see that..."")
- Explain the issue clearly
- Give 1-2 specific action steps
- End with encouragement

OUTPUT FORMAT:
Return a JSON object with:
{{
  ""mentorMessage"": ""Your complete mentor message in 2-3 short paragraphs""
}}

EXAMPLE GOOD OUTPUT:
{{
  ""mentorMessage"": ""Napansin ko na may 3 products na malapit nang maubos ang stock. Widget A is critically low - 0 units na lang! \n\nSuggest ko, restock agad ang Widget A today. For the other 2 items, set a reminder to order kapag umabot na sa reorder point. \n\nMabuti yan na vigilant ka sa inventory - hindi ka mauubusan ng benta! Keep it up! 💪""
}}

Keep responses concise (150-200 words max).";
        }

        /// <summary>
        /// Builds the user prompt with findings data.
        /// </summary>
        private string BuildUserPrompt(List<DetectorFinding> findings)
        {
            // Serialize findings as JSON for Gemini
            var findingsJson = JsonSerializer.Serialize(findings.Select(f => new
            {
                severity = f.Severity,
                title = f.Title,
                description = f.Description,
                entityName = f.EntityName,
                metricValue = f.MetricValue,
                thresholdValue = f.ThresholdValue
            }), new JsonSerializerOptions { WriteIndented = true });

            return $@"Here are the business insights I detected:

{findingsJson}

Please provide mentor-style coaching for these findings in Filipino/Taglish.";
        }

        /// <summary>
        /// Extracts mentor text from LLM JSON response.
        /// </summary>
        private string ExtractMentorText(JsonDocument response)
        {
            try
            {
                // Try to extract from standard response structure
                if (response.RootElement.TryGetProperty("mentorMessage", out var mentorMsg))
                {
                    return mentorMsg.GetString() ?? CreateGenericMessage();
                }

                // Fallback: check for common LLM response patterns
                if (response.RootElement.TryGetProperty("message", out var msg))
                {
                    return msg.GetString() ?? CreateGenericMessage();
                }

                if (response.RootElement.TryGetProperty("text", out var txt))
                {
                    return txt.GetString() ?? CreateGenericMessage();
                }

                // Last resort: stringify the entire response
                _logger.LogWarning("[BusinessMentorFormatter] Unexpected response structure, using full JSON");
                return response.RootElement.GetRawText();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[BusinessMentorFormatter] Failed to extract mentor text from response");
                return CreateGenericMessage();
            }
        }

        /// <summary>
        /// Creates fallback text when LLM formatting fails.
        /// </summary>
        private string CreateFallbackText(string category, List<DetectorFinding> findings)
        {
            var criticalCount = findings.Count(f => f.Severity == "critical");
            var warningCount = findings.Count(f => f.Severity == "warning");

            var categoryName = category switch
            {
                "inventory" => "Inventory",
                "finance" => "Budget",
                "sales" => "Sales",
                _ => "Business"
            };

            var summary = $"{categoryName} Alert: ";
            
            if (criticalCount > 0)
            {
                summary += $"{criticalCount} critical issue{(criticalCount > 1 ? "s" : "")} detected. ";
            }
            
            if (warningCount > 0)
            {
                summary += $"{warningCount} warning{(warningCount > 1 ? "s" : "")} found. ";
            }

            // Add top 3 findings
            var topFindings = findings
                .OrderByDescending(f => f.Severity == "critical" ? 2 : (f.Severity == "warning" ? 1 : 0))
                .Take(3)
                .Select(f => $"• {f.Title}: {f.Description}");

            summary += "\n\n" + string.Join("\n", topFindings);

            _logger.LogWarning(
                "[BusinessMentorFormatter] Using fallback text for category: {Category}",
                category);

            return summary;
        }

        /// <summary>
        /// Creates a generic mentor message when extraction fails.
        /// </summary>
        private string CreateGenericMessage()
        {
            return "I've reviewed your business data and found some insights worth discussing. " +
                   "Check the details above and let me know if you need help understanding any of these findings.";
        }

        // ========================================
        // SPECIALIZED FORMATTERS (Sprint 2)
        // ========================================

        /// <summary>
        /// Formats morning briefing from top 3 critical issues.
        /// </summary>
        public async Task<string> FormatMorningBriefingAsync(
            List<CriticalIssue> issues,
            CancellationToken ct = default)
        {
            if (!issues.Any())
            {
                return "Good morning! 🌟 Your business is running smoothly today. All systems green!";
            }

            var systemPrompt = @"You are a friendly business mentor for small retail owners. 
Given 3 business issues, create a warm morning briefing that helps the owner prioritize their day.

TONE:
- Start with 'Good morning, Boss! ☀️'
- Be encouraging but direct
- Focus on action items, not just problems
- Use simple language

STRUCTURE:
1. Brief greeting
2. List 3 issues with priority and actionable advice
3. End with encouragement

OUTPUT FORMAT:
Return JSON: {""briefing"": ""your message here""}

Keep under 150 words total.";

            var userPrompt = $@"Today's Top Issues:
{JsonSerializer.Serialize(issues, new JsonSerializerOptions { WriteIndented = true })}

Create a morning briefing for the business owner.";

            try
            {
                var response = await _geminiClient.GenerateJsonAsync(systemPrompt, userPrompt, 0.7, ct);
                
                if (response.RootElement.TryGetProperty("briefing", out var briefingElement))
                {
                    return briefingElement.GetString() ?? CreateFallbackBriefing(issues);
                }

                return CreateFallbackBriefing(issues);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[FormatMorningBriefingAsync] Gemini call failed, using fallback");
                return CreateFallbackBriefing(issues);
            }
        }

        /// <summary>
        /// Formats inventory risk analysis with teaching.
        /// </summary>
        public async Task<string> FormatInventoryRiskAsync(
            ProductRiskData riskData,
            CancellationToken ct = default)
        {
            var systemPrompt = @"You are teaching a new business owner about inventory management.
Explain the financial risk of this product's current stock situation.

CONCEPTS TO TEACH:
- If stockout risk: Explain 'opportunity cost' (money lost from not having stock to sell)
- If dead stock: Explain 'carrying costs' (money tied up in unsold inventory)

TONE:
- Educational but friendly
- Include specific numbers (₱ amounts, days)
- Provide actionable advice

OUTPUT FORMAT:
Return JSON: {""analysis"": ""your message here""}

Max 120 words.";

            var userPrompt = $@"Product Risk Analysis:
{JsonSerializer.Serialize(riskData, new JsonSerializerOptions { WriteIndented = true })}

Teach the owner about this inventory situation.";

            try
            {
                var response = await _geminiClient.GenerateJsonAsync(systemPrompt, userPrompt, 0.5, ct);
                
                if (response.RootElement.TryGetProperty("analysis", out var analysisElement))
                {
                    return analysisElement.GetString() ?? CreateFallbackInventoryRisk(riskData);
                }

                return CreateFallbackInventoryRisk(riskData);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[FormatInventoryRiskAsync] Gemini call failed, using fallback");
                return CreateFallbackInventoryRisk(riskData);
            }
        }

        /// <summary>
        /// Formats profit margin teaching.
        /// </summary>
        public async Task<string> FormatProfitTeachingAsync(
            List<MarginData> margins,
            CancellationToken ct = default)
        {
            if (!margins.Any())
            {
                return "Not enough sales data to analyze profit margins yet. Keep tracking your sales!";
            }

            var systemPrompt = @"You are teaching a business owner the difference between 'revenue' (cash coming in) and 'profit' (money you keep).

KEY TEACHING POINTS:
1. Why high revenue ≠ success (could have low margins)
2. What a 'healthy' profit margin looks like for retail (aim for 30%+)
3. Which category needs attention (lowest margin)
4. Actionable advice: negotiate supplier costs OR raise prices

Use the analogy: 'Revenue is vanity, profit is sanity.'

OUTPUT FORMAT:
Return JSON: {""teaching"": ""your message here""}

Max 150 words.";

            var userPrompt = $@"Sales Performance by Category:
{JsonSerializer.Serialize(margins, new JsonSerializerOptions { WriteIndented = true })}

Teach the owner about profit margins.";

            try
            {
                var response = await _geminiClient.GenerateJsonAsync(systemPrompt, userPrompt, 0.5, ct);
                
                if (response.RootElement.TryGetProperty("teaching", out var teachingElement))
                {
                    return teachingElement.GetString() ?? CreateFallbackProfitTeaching(margins);
                }

                return CreateFallbackProfitTeaching(margins);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[FormatProfitTeachingAsync] Gemini call failed, using fallback");
                return CreateFallbackProfitTeaching(margins);
            }
        }

        /// <summary>
        /// Formats expense forecast with seasonality teaching.
        /// </summary>
        public async Task<string> FormatExpenseForecastAsync(
            List<SpendingTrend> trends,
            CancellationToken ct = default)
        {
            if (!trends.Any())
            {
                return "Not enough expense data to analyze trends yet. Keep tracking your expenses!";
            }

            var systemPrompt = @"You are teaching a business owner about cash flow planning and seasonal spending patterns.

KEY TEACHING POINTS:
1. Why the spending spike/drop occurred this month
2. Is this spike HEALTHY (e.g., stocking up before holiday rush) or UNHEALTHY (waste, overspending)?
3. Teach the concept of 'seasonality' in retail
4. Advice on cash flow planning (spend now to earn later)

Use the phrase: 'Smart spending today = profits tomorrow.'

OUTPUT FORMAT:
Return JSON: {""forecast"": ""your message here""}

Max 150 words.";

            var userPrompt = $@"Monthly Spending Analysis:
{JsonSerializer.Serialize(trends, new JsonSerializerOptions { WriteIndented = true })}

Teach the owner about expense patterns.";

            try
            {
                var response = await _geminiClient.GenerateJsonAsync(systemPrompt, userPrompt, 0.5, ct);
                
                if (response.RootElement.TryGetProperty("forecast", out var forecastElement))
                {
                    return forecastElement.GetString() ?? CreateFallbackExpenseForecast(trends);
                }

                return CreateFallbackExpenseForecast(trends);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[FormatExpenseForecastAsync] Gemini call failed, using fallback");
                return CreateFallbackExpenseForecast(trends);
            }
        }

        // ========================================
        // FALLBACK METHODS
        // ========================================

        private string CreateFallbackBriefing(List<CriticalIssue> issues)
        {
            var message = "Good morning, Boss! ☀️ Here's your game plan for today:\n\n";
            for (int i = 0; i < issues.Count; i++)
            {
                var issue = issues[i];
                var priority = issue.Severity == "critical" ? "URGENT" : "ACTION";
                message += $"{i + 1}. [{priority}] {issue.Title}: {issue.Description}\n";
            }
            message += "\nFocus on these priorities to keep your business running smoothly!";
            return message;
        }

        private string CreateFallbackInventoryRisk(ProductRiskData riskData)
        {
            if (riskData.RiskType == "stockout")
            {
                return $"⚠️ Stockout Risk Detected: {riskData.ProductName} has only {riskData.CurrentStock} units left. " +
                       $"With average daily sales of {riskData.AverageDailySales:F1} units, you could run out in {riskData.DaysUntilStockout} days. " +
                       $"Potential lost revenue: ₱{riskData.PotentialLostRevenue:F2}. Reorder now to avoid losing sales!";
            }
            else if (riskData.RiskType == "dead_stock")
            {
                return $"📦 Dead Stock Alert: {riskData.ProductName} hasn't sold in {riskData.DaysSinceLastSale} days. " +
                       $"Consider running a promotion to move this inventory and free up cash.";
            }
            return $"{riskData.ProductName} inventory levels are healthy. Current stock: {riskData.CurrentStock} units.";
        }

        private string CreateFallbackProfitTeaching(List<MarginData> margins)
        {
            var lowestMargin = margins.OrderBy(m => m.ProfitMarginPercent).First();
            var avgMargin = margins.Average(m => m.ProfitMarginPercent);
            
            return $"💰 Profit Margin Analysis:\n\n" +
                   $"Average profit margin: {avgMargin:F1}% (Retail target: 30%+)\n" +
                   $"Lowest margin: {lowestMargin.Category} at {lowestMargin.ProfitMarginPercent:F1}%\n\n" +
                   $"Remember: Revenue is vanity, profit is sanity. Focus on improving margins in {lowestMargin.Category} " +
                   $"by negotiating better supplier prices or adjusting your pricing strategy.";
        }

        private string CreateFallbackExpenseForecast(List<SpendingTrend> trends)
        {
            var biggestAnomaly = trends.FirstOrDefault(t => t.IsAnomaly);
            if (biggestAnomaly != null)
            {
                var change = biggestAnomaly.AnomalyType == "spike" ? "spike" : "drop";
                return $"📊 Expense Trend Alert:\n\n" +
                       $"{biggestAnomaly.Category} shows a {change} of {Math.Abs(biggestAnomaly.PercentDeviation):F0}% this month. " +
                       $"Spent ₱{biggestAnomaly.TotalSpent:F2} vs average ₱{biggestAnomaly.AverageMonthlySpend:F2}.\n\n" +
                       $"This could be seasonal (e.g., stocking up before peak season) or an outlier. " +
                       $"Review this category to ensure the spending aligns with your business goals.";
            }
            return "Your expense patterns look normal this month. Continue monitoring for any unusual trends.";
        }
    }
}
