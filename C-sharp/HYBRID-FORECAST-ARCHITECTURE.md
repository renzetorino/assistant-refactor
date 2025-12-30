# Hybrid EMA/CMA Forecasting Architecture

## System Flow Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                         USER REQUEST                             │
│         "Forecast sales for next 30 days"                        │
└──────────────────────┬──────────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────────────┐
│                    API ENDPOINT                                  │
│   GET /api/forecast/sales?days=30&alpha=0.2&beta=0.7           │
│                                                                  │
│   ForecastController.cs                                          │
│   ├─ Validate parameters                                        │
│   ├─ Route to HybridForecastService                            │
│   └─ Return JSON response                                       │
└──────────────────────┬──────────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────────────┐
│              HYBRID FORECAST SERVICE                             │
│   HybridForecastService.cs                                       │
│                                                                  │
│   1. Load Historical Data                                        │
│      ├─ SqlCatalog.GetSalesDailySeriesAsync()                  │
│      └─ Fill gaps (missing dates → 0)                          │
│                                                                  │
│   2. Calculate Trends                                            │
│      ├─ EMA: α * current + (1-α) * previous                    │
│      ├─ CMA: Centered moving average (window=14 or 28)        │
│      └─ Blend: β * EMA + (1-β) * CMA                          │
│                                                                  │
│   3. Apply Seasonality                                           │
│      └─ Weekday factors (if ≥42 days data)                     │
│                                                                  │
│   4. Generate Forecast                                           │
│      ├─ Point estimate = Hybrid trend × seasonality            │
│      └─ Confidence intervals = ±1.96σ × horizon factor         │
│                                                                  │
│   5. Calculate Metrics                                           │
│      ├─ MAPE (7-day backtest)                                  │
│      ├─ MAE (mean absolute error)                              │
│      └─ RMSE (root mean square error)                          │
│                                                                  │
│   6. Generate Narrative                                          │
│      ├─ Trend analysis (EMA vs CMA)                            │
│      ├─ Accuracy assessment                                     │
│      └─ Volatility insights                                     │
└──────────────────────┬──────────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────────────┐
│                    JSON RESPONSE                                 │
│                                                                  │
│   {                                                              │
│     "series": {                                                  │
│       "history": [...],                                          │
│       "forecast": [                                              │
│         { date, value, lower, upper }                           │
│       ]                                                          │
│     },                                                           │
│     "kpis": {                                                    │
│       "method": "hybrid_ema_cma",                               │
│       "ema_value": 1350.00,                                     │
│       "cma_value": 1200.00,                                     │
│       "hybrid_trend": 1305.00,                                  │
│       "mape_7d": 8.5,                                           │
│       ...                                                        │
│     },                                                           │
│     "notes": {                                                   │
│       "narrative": "Recent trends show..."                      │
│     }                                                            │
│   }                                                              │
└──────────────────────┬──────────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────────────┐
│                  FRONTEND DISPLAY                                │
│   SalesForecastWindow.jsx                                        │
│                                                                  │
│   ┌──────────────────────────────────────────────────────┐    │
│   │  Sales Forecast                                       │    │
│   │  [Period Badge] [Method: Hybrid EMA/CMA Badge]      │    │
│   └──────────────────────────────────────────────────────┘    │
│                                                                  │
│   ┌──────────────────────────────────────────────────────┐    │
│   │  KPIs                                                 │    │
│   │  Forecast (30d): ₱39,000    Last 7d: ₱8,500         │    │
│   │  Last 28d: ₱35,000                                   │    │
│   └──────────────────────────────────────────────────────┘    │
│                                                                  │
│   ┌──────────────────────────────────────────────────────┐    │
│   │  Forecast Model Details                              │    │
│   │  EMA: ₱1,350 (α=0.20)  │  CMA: ₱1,200              │    │
│   │  Hybrid: ₱1,305 (β=0.70) │ MAPE: 8.5% [High]       │    │
│   └──────────────────────────────────────────────────────┘    │
│                                                                  │
│   ┌──────────────────────────────────────────────────────┐    │
│   │  Analyst Note                                         │    │
│   │  "Recent trends show an upward trajectory..."        │    │
│   └──────────────────────────────────────────────────────┘    │
│                                                                  │
│   ┌──────────────────────────────────────────────────────┐    │
│   │  Forecast Table (daily breakdown)                    │    │
│   └──────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────┘
```

---

## Component Interaction

```
┌──────────────┐      ┌──────────────┐      ┌──────────────┐
│              │      │              │      │              │
│  EMAService  │─────▶│   Hybrid     │◀────▶│  SqlCatalog  │
│              │      │  Forecast    │      │              │
│  - Calculate │      │   Service    │      │  - Load data │
│  - Adaptive  │      │              │      │  - Daily     │
│  - CI bands  │      │  - Blend     │      │    series    │
│              │      │  - Seasonality│      │              │
└──────────────┘      │  - Metrics   │      └──────────────┘
                      │  - Narrative │
                      └──────┬───────┘
                             │
                    ┌────────┴────────┐
                    │                 │
         ┌──────────▼─────┐  ┌───────▼────────┐
         │                │  │                 │
         │  Forecast      │  │   Forecast      │
         │  Controller    │  │   Store         │
         │                │  │   (VEC DB)      │
         │  - Validate    │  │                 │
         │  - Route       │  │  - Save         │
         │  - Response    │  │  - Retrieve     │
         │                │  │  - History      │
         └────────────────┘  └─────────────────┘
```

---

## Data Flow: Historical → Forecast

```
Historical Data (180 days)
  │
  ├─► Fill Gaps (0 for missing dates)
  │
  ├─► Calculate EMA (recent trend)
  │    Formula: EMA_t = α×X_t + (1-α)×EMA_{t-1}
  │    
  ├─► Calculate CMA (long-term average)
  │    Formula: CMA_t = Σ(X_i) / t
  │
  ├─► Blend Trends
  │    Formula: Hybrid = β×EMA + (1-β)×CMA
  │
  ├─► Detect Seasonality (if ≥42 days)
  │    Weekday factors: Mon, Tue, ..., Sun
  │
  ├─► Generate Forecast Points (1 to N days)
  │    For each day:
  │      1. Base = Hybrid trend
  │      2. Apply seasonality factor
  │      3. Calculate CI: Base ± 1.96σ × (1 + horizon/N × 0.5)
  │
  └─► Calculate Accuracy Metrics
       - Fit historical data
       - Calculate residuals
       - MAPE = mean(|actual - forecast| / actual) × 100
       - MAE = mean(|residuals|)
       - RMSE = sqrt(mean(residuals²))
```

---

## Algorithm Comparison

### Legacy (CMA Only)
```
Input: Historical data (180 days)
  ↓
Centered Moving Average (window=14 or 28)
  ↓
Apply Seasonality
  ↓
Forecast = CMA × Seasonality
  ↓
Confidence Interval = Forecast ± 1.28σ
```

### New (Hybrid EMA/CMA)
```
Input: Historical data (180 days)
  ↓
Calculate EMA (α=0.2)  ←─ Recent trend
  ↓
Calculate CMA (window=14 or 28)  ←─ Long-term baseline
  ↓
Blend: β×EMA + (1-β)×CMA  (β=0.7)
  ↓
Apply Seasonality
  ↓
Forecast = Hybrid × Seasonality
  ↓
Dynamic CI = Forecast ± 1.96σ × (1 + horizon_factor)
  ↓
Calculate MAPE, MAE, RMSE
  ↓
Generate Dynamic Narrative
```

---

## Key Advantages

| Aspect | Legacy (CMA) | Hybrid (EMA/CMA) |
|--------|-------------|------------------|
| **Responsiveness** | Low | High |
| **Stability** | High | Balanced |
| **Trend Detection** | Slow | Fast |
| **Volatility Handling** | Poor | Good |
| **Accuracy Metrics** | None | MAPE, MAE, RMSE |
| **Narratives** | Static | Dynamic |
| **Confidence** | Fixed | Time-expanding |
| **Customization** | None | α, β parameters |

---

## Performance Characteristics

```
Time Complexity:
├─ EMA calculation: O(n)
├─ CMA calculation: O(n²)  ← Dominant
├─ Seasonality: O(n)
├─ Forecast generation: O(h)  where h = horizon
└─ Total: O(n²)

Space Complexity:
├─ Historical data: O(n)
├─ Trend arrays: O(n)
├─ Forecast points: O(h)
└─ Total: O(n + h)

Typical Performance:
├─ 180 days historical: ~50ms
├─ 30 days forecast: ~10ms
└─ Total processing: ~100-150ms
```

---

## Error Handling

```
┌─────────────────────────────────────┐
│   Request Validation                │
├─────────────────────────────────────┤
│ ✓ days ∈ [1, 90]                   │
│ ✓ alpha ∈ [0.05, 0.5]              │
│ ✓ beta ∈ [0, 1]                    │
└───────────┬─────────────────────────┘
            │
            ▼
┌─────────────────────────────────────┐
│   Data Availability Check           │
├─────────────────────────────────────┤
│ < 7 days → Simple average fallback  │
│ 7-42 days → No seasonality         │
│ ≥ 42 days → Full hybrid + season   │
└───────────┬─────────────────────────┘
            │
            ▼
┌─────────────────────────────────────┐
│   Graceful Degradation              │
├─────────────────────────────────────┤
│ - Wide confidence intervals         │
│ - Warning in narrative              │
│ - Method = "simple_average"         │
└─────────────────────────────────────┘
```

---

**Version**: 1.0.0  
**Last Updated**: October 11, 2025  
**Status**: Production Ready ✅
