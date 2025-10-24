// dataAccess/Services/EMAService.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace dataAccess.Services;

/// <summary>
/// Exponential Moving Average (EMA) calculation service.
/// EMA gives more weight to recent data points, making it responsive to trends.
/// Formula: EMA_t = α * X_t + (1 - α) * EMA_{t-1}
/// </summary>
public sealed class EMAService
{
    /// <summary>
    /// Calculate EMA for a given dataset.
    /// </summary>
    /// <param name="values">Time-series data points</param>
    /// <param name="alpha">Smoothing factor (0 &lt; α ≤ 1). Higher α = more responsive to recent changes.</param>
    /// <returns>Array of EMA values</returns>
    public static double[] Calculate(decimal[] values, double alpha = 0.2)
    {
        if (values == null || values.Length == 0)
            return Array.Empty<double>();

        if (alpha <= 0 || alpha > 1)
            throw new ArgumentException("Alpha must be between 0 and 1", nameof(alpha));

        var ema = new double[values.Length];
        
        // Initialize with first value
        ema[0] = (double)values[0];

        // Calculate EMA for subsequent values
        for (int i = 1; i < values.Length; i++)
        {
            ema[i] = alpha * (double)values[i] + (1 - alpha) * ema[i - 1];
        }

        return ema;
    }

    /// <summary>
    /// Calculate EMA with adaptive alpha based on volatility.
    /// Higher volatility → higher alpha (more responsive).
    /// </summary>
    public static double[] CalculateAdaptive(decimal[] values, double baseAlpha = 0.2, double volatilityWindow = 7)
    {
        if (values == null || values.Length == 0)
            return Array.Empty<double>();

        var ema = new double[values.Length];
        ema[0] = (double)values[0];

        for (int i = 1; i < values.Length; i++)
        {
            // Calculate local volatility
            int start = Math.Max(0, i - (int)volatilityWindow);
            var window = values.Skip(start).Take(i - start + 1).Select(v => (double)v).ToArray();
            double volatility = window.Length > 1 ? StandardDeviation(window) / window.Average() : 0;

            // Adjust alpha based on volatility (clamp between 0.1 and 0.5)
            double alpha = Math.Clamp(baseAlpha * (1 + volatility), 0.1, 0.5);

            ema[i] = alpha * (double)values[i] + (1 - alpha) * ema[i - 1];
        }

        return ema;
    }

    /// <summary>
    /// Calculate confidence intervals for EMA forecast.
    /// </summary>
    public static (double lower, double upper) CalculateConfidenceInterval(
        double emaValue, 
        double[] residuals, 
        double confidenceLevel = 1.96) // 95% CI
    {
        if (residuals == null || residuals.Length == 0)
            return (emaValue, emaValue);

        double sigma = StandardDeviation(residuals);
        double margin = confidenceLevel * sigma;

        return (Math.Max(0, emaValue - margin), emaValue + margin);
    }

    private static double StandardDeviation(double[] values)
    {
        if (values.Length <= 1) return 0;

        double mean = values.Average();
        double variance = values.Sum(v => Math.Pow(v - mean, 2)) / (values.Length - 1);
        return Math.Sqrt(variance);
    }
}
