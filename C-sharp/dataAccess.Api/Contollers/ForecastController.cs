// dataAccess.Api/Controllers/ForecastController.cs
using Microsoft.AspNetCore.Mvc;
using dataAccess.Services;

namespace dataAccess.Api.Controllers;

[ApiController]
[Route("api/forecast")]
public sealed class ForecastController : ControllerBase
{
    private readonly HybridForecastService _hybridSvc;
    
#pragma warning disable CS0618 // Type or member is obsolete
    private readonly SimpleForecastService _legacySvc;

    public ForecastController(HybridForecastService hybridSvc, SimpleForecastService legacySvc)
#pragma warning restore CS0618
    {
        _hybridSvc = hybridSvc;
        _legacySvc = legacySvc;
    }

    /// <summary>
    /// Generate sales forecast using hybrid EMA/CMA approach.
    /// </summary>
    /// <param name="days">Forecast horizon (1-90 days)</param>
    /// <param name="alpha">EMA smoothing factor (0.1-0.5, default: 0.2)</param>
    /// <param name="beta">EMA vs CMA blend weight (0-1, default: 0.7)</param>
    /// <param name="from">Historical data start date</param>
    /// <param name="to">Historical data end date</param>
    /// <param name="legacy">Use legacy CMA-only method (deprecated)</param>
    [HttpGet("sales")]
    public async Task<IActionResult> Sales(
        [FromQuery] int days = 30,
        [FromQuery] double alpha = 0.2,
        [FromQuery] double beta = 0.7,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] bool legacy = false,
        CancellationToken ct = default)
    {
        if (legacy)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            return Ok(await _legacySvc.ForecastAsync(ForecastDomain.Sales, Clamp(days), from, to, ct));
#pragma warning restore CS0618
        }

        return Ok(await _hybridSvc.ForecastAsync(
            ForecastDomain.Sales,
            Clamp(days),
            ClampAlpha(alpha),
            ClampBeta(beta),
            from,
            to,
            ct));
    }

    /// <summary>
    /// Generate expenses forecast using hybrid EMA/CMA approach.
    /// </summary>
    [HttpGet("expenses")]
    public async Task<IActionResult> Expenses(
        [FromQuery] int days = 30,
        [FromQuery] double alpha = 0.2,
        [FromQuery] double beta = 0.7,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] bool legacy = false,
        CancellationToken ct = default)
    {
        if (legacy)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            return Ok(await _legacySvc.ForecastAsync(ForecastDomain.Expenses, Clamp(days), from, to, ct));
#pragma warning restore CS0618
        }

        return Ok(await _hybridSvc.ForecastAsync(
            ForecastDomain.Expenses,
            Clamp(days),
            ClampAlpha(alpha),
            ClampBeta(beta),
            from,
            to,
            ct));
    }

    private static int Clamp(int d) => d <= 0 ? 30 : Math.Min(d, 90);
    private static double ClampAlpha(double a) => Math.Clamp(a, 0.05, 0.5);
    private static double ClampBeta(double b) => Math.Clamp(b, 0.0, 1.0);
}

