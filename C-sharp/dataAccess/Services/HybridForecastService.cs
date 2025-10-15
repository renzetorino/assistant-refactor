// dataAccess/Services/HybridForecastService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace dataAccess.Services;

/// <summary>
/// Hybrid forecasting service that combines EMA (short-term trends) 
/// with CMA (long-term stability) for improved accuracy.
/// 
/// Strategy:
/// - EMA: Captures recent trends and sudden changes
/// - CMA: Provides stable baseline from historical average
/// - Blend: Weighted combination based on forecast horizon
/// </summary>
public sealed class HybridForecastService
{
    private readonly ISqlCatalog _sql;

    public HybridForecastService(ISqlCatalog sql) => _sql = sql;

    /// <summary>
    /// Generate hybrid forecast combining EMA and CMA.
    /// </summary>
    /// <param name="domain">Sales or Expenses</param>
    /// <param name="days">Forecast horizon in days</param>
    /// <param name="emaAlpha">EMA smoothing factor (default: 0.2)</param>
    /// <param name="blendBeta">EMA vs CMA weight (0-1). Higher = more EMA influence.</param>
    /// <param name="from">Historical data start date</param>
    /// <param name="to">Historical data end date</param>
    public async Task<object> ForecastAsync(
        ForecastDomain domain,
        int days = 30,
        double emaAlpha = 0.2,
        double blendBeta = 0.7,
        DateOnly? from = null,
        DateOnly? to = null,
        CancellationToken ct = default)
    {
        // Validate parameters
        if (emaAlpha <= 0 || emaAlpha > 1)
            throw new ArgumentException("EMA alpha must be between 0 and 1", nameof(emaAlpha));
        
        if (blendBeta < 0 || blendBeta > 1)
            throw new ArgumentException("Blend beta must be between 0 and 1", nameof(blendBeta));

        var today = TodayPH();
        var histEnd = to ?? today;
        var histStart = from ?? histEnd.AddDays(-180);

        // Load historical data
        var history = domain == ForecastDomain.Sales
            ? await LoadDailySeriesSales(histStart, histEnd, ct)
            : await LoadDailySeriesExpenses(histStart, histEnd, ct);

        history = FillGaps(histStart, histEnd, history);
        var values = history.Select(x => x.value).ToArray();

        // Handle insufficient data
        if (history.Count < 7)
        {
            return BuildMinimalForecast(history, histEnd, days);
        }

        // Calculate EMA
        var ema = EMAService.Calculate(values, emaAlpha);

        // Calculate CMA (using centered moving average for historical; simple average for forecast)
        int cmaWindow = history.Count >= 56 ? 28 : 14;
        var cma = CenteredMA(values, cmaWindow);
        ReplaceNaWithRolling(values, cma, Math.Max(7, cmaWindow / 2));

        // Apply seasonality if enough data
        decimal[]? weekdayFactors = history.Count >= 42 ? WeekdayFactors(history, cma) : null;

        // Calculate hybrid trend (blend EMA and CMA)
        var hybridTrend = BlendTrends(ema, cma, blendBeta);

        // Fit model and calculate residuals
        var fitted = Fit(values, hybridTrend, weekdayFactors, history);
        var residuals = values.Zip(fitted, (y, yh) => (double)y - yh).ToArray();
        var sigma = StandardDeviation(residuals);

        // Generate forecast
        var lastHybridTrend = (decimal)hybridTrend[^1];
        var lastEMA = (decimal)ema[^1];
        var lastCMA = (decimal)cma[^1];
        
        var forecastPoints = BuildHybridForecast(
            histEnd, 
            days, 
            lastHybridTrend,
            lastEMA,
            lastCMA,
            weekdayFactors, 
            (decimal)sigma,
            residuals);

        // Calculate accuracy metrics
        var metrics = CalculateAccuracyMetrics(values, fitted, residuals);

        return WrapResult(
            history, 
            forecastPoints, 
            histEnd, 
            days, 
            metrics,
            emaAlpha,
            blendBeta,
            lastEMA,
            lastCMA,
            lastHybridTrend);
    }

    // ========== Data Loading ==========
    private async Task<List<(DateOnly date, decimal value)>> LoadDailySeriesSales(
        DateOnly start, DateOnly end, CancellationToken ct)
    {
        var mi = _sql.GetType().GetMethod("GetSalesDailySeriesAsync");
        if (mi != null)
        {
            var task = (Task)mi.Invoke(_sql, new object?[] { start, end, ct })!;
            await task.ConfigureAwait(false);
            var result = (IReadOnlyList<(DateOnly Date, decimal Value)>)
                task.GetType().GetProperty("Result")!.GetValue(task)!;
            return result.Select(x => (x.Date, x.Value)).ToList();
        }

        var rows = (IEnumerable<dynamic>)(await _sql.RunAsync("SALES_BY_DAY",
            new Dictionary<string, object?> { ["start"] = start, ["end"] = end }, ct))!;
        return rows.Select(r => ((DateOnly)r.date, (decimal)r.revenue)).ToList();
    }

    private async Task<List<(DateOnly date, decimal value)>> LoadDailySeriesExpenses(
        DateOnly start, DateOnly end, CancellationToken ct)
    {
        var mi = _sql.GetType().GetMethod("GetExpensesDailySeriesAsync");
        if (mi != null)
        {
            var task = (Task)mi.Invoke(_sql, new object?[] { start, end, ct })!;
            await task.ConfigureAwait(false);
            var result = (IReadOnlyList<(DateOnly Date, decimal Value)>)
                task.GetType().GetProperty("Result")!.GetValue(task)!;
            return result.Select(x => (x.Date, x.Value)).ToList();
        }

        var rows = (IEnumerable<dynamic>)(await _sql.RunAsync("EXPENSE_BY_DAY",
            new Dictionary<string, object?> { ["start"] = start, ["end"] = end }, ct))!;
        return rows.Select(r => ((DateOnly)r.date, (decimal)r.total)).ToList();
    }

    // ========== Trend Blending ==========
    private static double[] BlendTrends(double[] ema, double[] cma, double beta)
    {
        var blended = new double[ema.Length];
        for (int i = 0; i < ema.Length; i++)
        {
            blended[i] = beta * ema[i] + (1 - beta) * cma[i];
        }
        return blended;
    }

    // ========== Forecasting ==========
    private static List<ForecastPoint> BuildHybridForecast(
        DateOnly lastHist,
        int days,
        decimal hybridTrend,
        decimal emaValue,
        decimal cmaValue,
        decimal[]? weekdayFactors,
        decimal sigma,
        double[] residuals)
    {
        var points = new List<ForecastPoint>(days);
        
        for (int i = 1; i <= days; i++)
        {
            var date = lastHist.AddDays(i);
            
            // Use hybrid trend as base
            decimal value = hybridTrend;
            
            // Apply seasonality
            if (weekdayFactors is not null)
                value *= weekdayFactors[(int)date.DayOfWeek];
            
            value = Math.Max(0m, decimal.Round(value, 2));
            
            // Calculate confidence interval (wider for longer horizons)
            var horizonFactor = 1.0m + (i / (decimal)days) * 0.5m; // Increases uncertainty over time
            var margin = decimal.Round(1.96m * sigma * horizonFactor, 2);
            
            var lower = Math.Max(0m, value - margin);
            var upper = value + margin;
            
            points.Add(new ForecastPoint(date, value, lower, upper));
        }
        
        return points;
    }

    // ========== Accuracy Metrics ==========
    private static Dictionary<string, double?> CalculateAccuracyMetrics(
        decimal[] actual, 
        double[] fitted, 
        double[] residuals)
    {
        var metrics = new Dictionary<string, double?>();
        
        if (actual.Length < 7) return metrics;

        // MAPE (Mean Absolute Percentage Error) - last 7 days
        int n = 0;
        double mapeSum = 0;
        for (int i = actual.Length - 7; i < actual.Length; i++)
        {
            if (actual[i] <= 0) continue;
            mapeSum += Math.Abs(((double)actual[i] - fitted[i]) / (double)actual[i]);
            n++;
        }
        if (n > 0) metrics["mape_7d"] = (mapeSum / n) * 100.0;

        // MAE (Mean Absolute Error)
        metrics["mae"] = residuals.Select(Math.Abs).Average();

        // RMSE (Root Mean Square Error)
        metrics["rmse"] = Math.Sqrt(residuals.Select(r => r * r).Average());

        return metrics;
    }

    // ========== Helper Methods ==========
    private static DateOnly TodayPH()
    {
        string[] ids = OperatingSystem.IsWindows()
            ? new[] { "Singapore Standard Time", "Taipei Standard Time", "Malay Peninsula Standard Time" }
            : new[] { "Asia/Manila", "Asia/Singapore", "Asia/Taipei" };

        TimeZoneInfo? tz = null;
        foreach (var id in ids) { try { tz = TimeZoneInfo.FindSystemTimeZoneById(id); break; } catch { } }
        tz ??= TimeZoneInfo.Local;
        var nowPh = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        return DateOnly.FromDateTime(nowPh.Date);
    }

    private static List<(DateOnly date, decimal value)> FillGaps(
        DateOnly start, 
        DateOnly end, 
        IReadOnlyList<(DateOnly date, decimal value)> raw)
    {
        var dict = raw.ToDictionary(x => x.date, x => x.value);
        var filled = new List<(DateOnly, decimal)>();
        
        for (var dt = start; dt <= end; dt = dt.AddDays(1))
            filled.Add((dt, dict.TryGetValue(dt, out var v) ? v : 0m));
        
        return filled;
    }

    private static double[] CenteredMA(decimal[] arr, int window)
    {
        var a = arr.Select(x => (double)x).ToArray();
        var n = a.Length;
        var result = new double[n];
        Array.Fill(result, double.NaN);
        
        int halfWindow = window / 2;
        for (int i = halfWindow; i < n - halfWindow; i++)
        {
            double sum = 0;
            for (int j = i - halfWindow; j <= i + halfWindow - (window % 2 == 0 ? 1 : 0); j++)
                sum += a[j];
            result[i] = sum / window;
        }
        
        return result;
    }

    private static double[] Rolling(decimal[] arr, int window)
    {
        var a = arr.Select(x => (double)x).ToArray();
        var n = a.Length;
        var result = new double[n];
        
        for (int i = 0; i < n; i++)
        {
            int start = Math.Max(0, i - window + 1);
            double sum = 0;
            int count = 0;
            for (int j = start; j <= i; j++) { sum += a[j]; count++; }
            result[i] = sum / Math.Max(1, count);
        }
        
        return result;
    }

    private static void ReplaceNaWithRolling(decimal[] values, double[] trend, int window)
    {
        var roll = Rolling(values, window);
        for (int i = 0; i < trend.Length; i++)
            if (double.IsNaN(trend[i]))
                trend[i] = roll[i];
    }

    private static decimal[] WeekdayFactors(
        List<(DateOnly date, decimal value)> history, 
        double[] trend)
    {
        var buckets = Enumerable.Range(0, 7).Select(_ => new List<double>()).ToArray();
        
        for (int i = 0; i < history.Count; i++)
        {
            if (trend[i] <= 0 || double.IsNaN(trend[i])) continue;
            var ratio = (double)history[i].value / trend[i];
            buckets[(int)history[i].date.DayOfWeek].Add(ratio);
        }
        
        var factors = new decimal[7];
        double sum = 0;
        
        for (int d = 0; d < 7; d++)
        {
            var mean = buckets[d].Count == 0 ? 1.0 : buckets[d].Average();
            mean = Math.Clamp(mean, 0.6, 1.4);
            factors[d] = (decimal)mean;
            sum += mean;
        }
        
        // Normalize
        var norm = (decimal)(sum / 7.0);
        for (int d = 0; d < 7; d++)
            factors[d] /= norm;
        
        return factors;
    }

    private static double[] Fit(
        decimal[] actual, 
        double[] trend, 
        decimal[]? weekdayFactors, 
        List<(DateOnly date, decimal value)> history)
    {
        var fitted = new double[actual.Length];
        
        for (int i = 0; i < actual.Length; i++)
        {
            var baseValue = double.IsNaN(trend[i]) ? (double)history[i].value : trend[i];
            
            if (weekdayFactors is not null)
                baseValue *= (double)weekdayFactors[(int)history[i].date.DayOfWeek];
            
            fitted[i] = Math.Max(0, baseValue);
        }
        
        return fitted;
    }

    private static double StandardDeviation(double[] values)
    {
        if (values.Length <= 1) return 0;
        
        var mean = values.Average();
        var variance = values.Sum(v => (v - mean) * (v - mean)) / (values.Length - 1);
        return Math.Sqrt(variance);
    }

    private static object BuildMinimalForecast(
        List<(DateOnly date, decimal value)> history,
        DateOnly histEnd,
        int days)
    {
        var avgValue = history.Count > 0 ? history.Average(x => x.value) : 0m;
        var forecast = new List<ForecastPoint>();
        
        for (int i = 1; i <= days; i++)
        {
            var date = histEnd.AddDays(i);
            forecast.Add(new ForecastPoint(date, avgValue, avgValue * 0.8m, avgValue * 1.2m));
        }
        
        return new
        {
            series = new
            {
                history = history.Select(x => new { date = x.date, value = x.value }),
                forecast = forecast.Select(x => new { date = x.Date, value = x.Value, lower = x.Lower, upper = x.Upper })
            },
            kpis = new
            {
                horizon_days = days,
                sum_forecast = forecast.Sum(x => x.Value),
                last_7d_actual = history.TakeLast(7).Sum(x => x.value),
                method = "simple_average",
                confidence = "low"
            },
            notes = new
            {
                narrative = "Limited historical data available. Forecast based on simple average with wide confidence intervals."
            }
        };
    }

    private static object WrapResult(
        List<(DateOnly date, decimal value)> history,
        List<ForecastPoint> forecast,
        DateOnly histEnd,
        int days,
        Dictionary<string, double?> metrics,
        double emaAlpha,
        double blendBeta,
        decimal lastEMA,
        decimal lastCMA,
        decimal hybridTrend)
    {
        var last7 = history.TakeLast(7).Sum(x => x.value);
        var last28 = history.TakeLast(28).Sum(x => x.value);
        var sumForecast = forecast.Sum(x => x.Value);

        // Generate dynamic narrative
        var narrative = GenerateNarrative(lastEMA, lastCMA, hybridTrend, metrics, forecast);

        return new
        {
            series = new
            {
                history = history.Select(x => new { date = x.date, value = x.value }),
                forecast = forecast.Select(x => new 
                { 
                    date = x.Date, 
                    value = x.Value, 
                    lower = x.Lower, 
                    upper = x.Upper 
                })
            },
            kpis = new
            {
                horizon_days = days,
                sum_forecast = sumForecast,
                last_7d_actual = last7,
                last_28d_actual = last28,
                mape_7d = metrics.GetValueOrDefault("mape_7d"),
                mae = metrics.GetValueOrDefault("mae"),
                rmse = metrics.GetValueOrDefault("rmse"),
                method = "hybrid_ema_cma",
                ema_alpha = emaAlpha,
                blend_beta = blendBeta,
                ema_value = lastEMA,
                cma_value = lastCMA,
                hybrid_trend = hybridTrend
            },
            notes = new
            {
                narrative = narrative
            },
            period = new 
            { 
                label = $"{histEnd.AddDays(1):MMM d}–{histEnd.AddDays(days):MMM d, yyyy}",
                start = histEnd.AddDays(1),
                end = histEnd.AddDays(days)
            }
        };
    }

    private static string GenerateNarrative(
        decimal lastEMA,
        decimal lastCMA,
        decimal hybridTrend,
        Dictionary<string, double?> metrics,
        List<ForecastPoint> forecast)
    {
        var parts = new List<string>();

        // Trend analysis
        var emaCmaRatio = lastCMA > 0 ? (double)(lastEMA / lastCMA) : 1.0;
        if (emaCmaRatio > 1.1)
            parts.Add("Recent trends show an upward trajectory, suggesting increased activity in the near term.");
        else if (emaCmaRatio < 0.9)
            parts.Add("Recent trends indicate a downward movement compared to long-term averages.");
        else
            parts.Add("Current trends align closely with historical patterns, indicating stable conditions.");

        // Forecast confidence
        if (metrics.TryGetValue("mape_7d", out var mape) && mape.HasValue)
        {
            if (mape.Value < 10)
                parts.Add($"The model shows high accuracy with {mape.Value:F1}% error rate.");
            else if (mape.Value < 20)
                parts.Add($"Forecast reliability is moderate with {mape.Value:F1}% error rate.");
            else
                parts.Add($"Consider external factors as the model shows {mape.Value:F1}% variance.");
        }

        // Volatility analysis
        var avgForecast = forecast.Average(f => f.Value);
        var forecastVolatility = forecast.Count > 1 
            ? forecast.Average(f => Math.Abs((double)(f.Value - avgForecast))) / (double)avgForecast
            : 0;

        if (forecastVolatility > 0.2)
            parts.Add("Expect significant day-to-day variations in the forecast period.");
        else if (forecastVolatility < 0.1)
            parts.Add("Forecast shows consistent daily patterns with minimal volatility.");

        return string.Join(" ", parts);
    }
}
