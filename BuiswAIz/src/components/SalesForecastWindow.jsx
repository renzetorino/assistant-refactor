// src/components/SalesForecastWindow.jsx
import React, { useState } from "react";
import ForecastChart from "./ForecastChart";

export default function SalesForecastWindow({ forecast }) {
  const data = forecast ?? demoForecast;
  const [showDetails, setShowDetails] = useState(false);

  // Domain-aware title and color
  const domain = (data?.domain || "sales").toLowerCase();
  const isExpense = /expense/.test(domain);
  const title = isExpense ? "Expense Forecast" : "Sales Forecast";
  const primaryColor = isExpense ? "#d32f2f" : "#1976d2";
  const lightColor = isExpense ? "#ffebee" : "#e3f2fd";
  const icon = isExpense ? "💰" : "📈";

  const periodLabel =
    data?.period?.label || buildPeriodLabel(data?.period?.start, data?.period?.end) || "Period";

  const k = data?.kpis ?? {};
  const peso = (n) =>
    new Intl.NumberFormat("en-PH", { style: "currency", currency: "PHP", maximumFractionDigits: 0 }).format(n ?? 0);

  // Check if using hybrid method
  const isHybrid = k?.method === "hybrid_ema_cma";
  const methodLabel = isHybrid ? "AI-Enhanced Forecast" : "Standard Forecast";

  // Narrative sources
  const narrativeStr = typeof data?.notes?.narrative === "string" ? data.notes.narrative.trim() : "";
  const narrativeArr = Array.isArray(data?.narrative) ? data.narrative : [];

  // Calculate trend
  const getTrend = () => {
    if (!k.sum_forecast || !k.last_28d_actual) return null;
    const avgDaily = k.last_28d_actual / 28;
    const forecastDaily = k.sum_forecast / (k.horizon_days || 30);
    const change = ((forecastDaily - avgDaily) / avgDaily) * 100;
    return { change, isUp: change > 0 };
  };

  const trend = getTrend();

  return (
    <div className="pw-section" style={{ gridTemplateColumns: "1fr", maxWidth: 1200, margin: "0 auto" }}>
      {/* Header */}
      <div className="pw-card" style={{ background: `linear-gradient(135deg, ${lightColor} 0%, white 100%)`, border: `2px solid ${primaryColor}20` }}>
        <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between" }}>
          <div style={{ display: "flex", alignItems: "center", gap: 16 }}>
            <div style={{ fontSize: 48 }}>{icon}</div>
            <div>
              <h2 style={{ margin: 0, color: primaryColor, fontSize: 28 }}>{title}</h2>
              <div style={{ display: "flex", gap: 8, marginTop: 8, flexWrap: "wrap" }}>
                <span className="report-chip" style={{ background: primaryColor, color: "white", fontWeight: 600 }}>
                  {periodLabel}
                </span>
                <span className="report-chip" style={{ background: "#4caf50", color: "white" }}>
                  ✨ {methodLabel}
                </span>
                {isHybrid && k.mape_7d != null && (
                  <span 
                    className="report-chip" 
                    style={{ 
                      background: k.mape_7d < 10 ? "#4caf50" : k.mape_7d < 20 ? "#ff9800" : "#f44336",
                      color: "white"
                    }}
                  >
                    {k.mape_7d < 10 ? "🎯" : k.mape_7d < 20 ? "✓" : "⚠️"} 
                    {" "}{k.mape_7d.toFixed(1)}% accuracy
                  </span>
                )}
              </div>
            </div>
          </div>
        </div>
      </div>

      {/* Main KPI Cards */}
      <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(240px, 1fr))", gap: 16 }}>
        <KpiCard 
          label="Total Forecast" 
          value={peso(k.sum_forecast)} 
          sublabel={`${k.horizon_days ?? "?"} days ahead`}
          icon="🎯"
          color={primaryColor}
          trend={trend}
        />
        {"last_7d_actual" in k && (
          <KpiCard 
            label="Last 7 Days" 
            value={peso(k.last_7d_actual)} 
            sublabel="Historical average"
            icon="📊"
            color="#7c4dff"
          />
        )}
        {"last_28d_actual" in k && (
          <KpiCard 
            label="Last 28 Days" 
            value={peso(k.last_28d_actual)} 
            sublabel="Monthly baseline"
            icon="📅"
            color="#00acc1"
          />
        )}
      </div>

      {/* Chart Visualization */}
      <div className="pw-card">
        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: 16 }}>
          <h3 style={{ margin: 0 }}>📊 Forecast Trend</h3>
          <div style={{ fontSize: 12, color: "#666" }}>
            <span style={{ marginRight: 16 }}>● Historical Data</span>
            <span style={{ marginRight: 16 }}>● Forecast</span>
            <span>▢ Confidence Range</span>
          </div>
        </div>
        <ForecastChart data={data} domain={domain} />
      </div>

      {/* Insights Card */}
      {(narrativeStr || narrativeArr.length > 0) && (
        <div className="pw-card" style={{ background: "#f8f9fa", border: "2px solid #dee2e6" }}>
          <div style={{ display: "flex", alignItems: "center", gap: 12, marginBottom: 12 }}>
            <span style={{ fontSize: 32 }}>💡</span>
            <h3 style={{ margin: 0 }}>Key Insights</h3>
          </div>
          {narrativeStr && (
            <p style={{ fontSize: 16, lineHeight: 1.6, margin: 0, color: "#333" }}>
              {narrativeStr}
            </p>
          )}
          {!narrativeStr && narrativeArr.length > 0 && (
            <ul style={{ margin: "8px 0", paddingLeft: 20, lineHeight: 1.8 }}>
              {narrativeArr.slice(0, 5).map((t, i) => (
                <li key={i} style={{ fontSize: 15, color: "#333" }}>{t}</li>
              ))}
            </ul>
          )}
        </div>
      )}

      {/* Advanced Details (Collapsible) */}
      {isHybrid && (
        <div className="pw-card">
          <div 
            style={{ 
              display: "flex", 
              justifyContent: "space-between", 
              alignItems: "center",
              cursor: "pointer",
              userSelect: "none"
            }}
            onClick={() => setShowDetails(!showDetails)}
          >
            <h4 style={{ margin: 0 }}>🔧 Advanced Forecast Details</h4>
            <span style={{ fontSize: 20 }}>{showDetails ? "▼" : "▶"}</span>
          </div>
          
          {showDetails && (
            <div style={{ marginTop: 16, paddingTop: 16, borderTop: "1px solid #eee" }}>
              <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))", gap: 16 }}>
                <DetailItem 
                  label="Recent Trend (EMA)" 
                  value={peso(k.ema_value)} 
                  meta={`α = ${k.ema_alpha?.toFixed(2) ?? "0.20"}`}
                  color="#2e7d32"
                />
                <DetailItem 
                  label="Long-term Average (CMA)" 
                  value={peso(k.cma_value)} 
                  color="#1565c0"
                />
                <DetailItem 
                  label="Blended Forecast" 
                  value={peso(k.hybrid_trend)} 
                  meta={`β = ${k.blend_beta?.toFixed(2) ?? "0.70"}`}
                  color="#6a1b9a"
                />
                {k.mae != null && (
                  <DetailItem 
                    label="Mean Error (MAE)" 
                    value={peso(k.mae)} 
                    color="#ff6f00"
                  />
                )}
              </div>
              <div style={{ marginTop: 16, padding: 12, background: "#fff3e0", borderRadius: 6 }}>
                <div style={{ fontSize: 12, color: "#e65100", fontWeight: 600 }}>
                  ℹ️ How it works:
                </div>
                <div style={{ fontSize: 13, color: "#666", marginTop: 4, lineHeight: 1.5 }}>
                  This forecast uses AI-enhanced hybrid modeling that combines recent trends (EMA) 
                  with historical patterns (CMA) for more accurate predictions.
                </div>
              </div>
            </div>
          )}
        </div>
      )}
    </div>
  );
}

function KpiCard({ label, value, sublabel, icon, color, trend }) {
  return (
    <div 
      className="pw-card" 
      style={{ 
        background: `linear-gradient(135deg, ${color}10 0%, white 100%)`,
        border: `2px solid ${color}30`,
        transition: "transform 0.2s",
        cursor: "default"
      }}
      onMouseEnter={(e) => e.currentTarget.style.transform = "translateY(-4px)"}
      onMouseLeave={(e) => e.currentTarget.style.transform = "translateY(0)"}
    >
      <div style={{ display: "flex", alignItems: "start", justifyContent: "space-between" }}>
        <div style={{ flex: 1 }}>
          <div style={{ fontSize: 13, color: "#666", marginBottom: 4, fontWeight: 500 }}>{label}</div>
          <div style={{ fontSize: 28, fontWeight: 700, color: color, marginBottom: 4 }}>{value}</div>
          <div style={{ fontSize: 12, color: "#888" }}>{sublabel}</div>
          {trend && (
            <div style={{ 
              marginTop: 8, 
              fontSize: 13, 
              color: trend.isUp ? "#2e7d32" : "#d32f2f",
              fontWeight: 600
            }}>
              {trend.isUp ? "↗" : "↘"} {Math.abs(trend.change).toFixed(1)}% vs avg
            </div>
          )}
        </div>
        <div style={{ fontSize: 36 }}>{icon}</div>
      </div>
    </div>
  );
}

function DetailItem({ label, value, meta, color }) {
  return (
    <div>
      <div style={{ fontSize: 12, color: "#666", marginBottom: 4 }}>{label}</div>
      <div style={{ fontSize: 20, fontWeight: 600, color: color || "#333" }}>{value}</div>
      {meta && <div style={{ fontSize: 11, color: "#999", marginTop: 2 }}>{meta}</div>}
    </div>
  );
}

function buildPeriodLabel(start, end) {
  if (!start || !end) return null;
  try {
    const s = new Date(start), e = new Date(end);
    const sameYear = s.getUTCFullYear() === e.getUTCFullYear();
    const left = new Intl.DateTimeFormat("en-US", { month: "short", day: "numeric", timeZone: "UTC" }).format(s);
    const right = new Intl.DateTimeFormat("en-US", { month: "short", day: "numeric", year: "numeric", timeZone: "UTC" }).format(e);
    return sameYear ? `${left}–${right}` : `${left}, ${s.getUTCFullYear()}–${right}`;
  } catch { return null; }
}

const demoForecast = {
  domain: "sales",
  period: { label: "Oct 7–Nov 5, 2025" },
  kpis: {
    sum_forecast: 5394.98,
    last_7d_actual: 4733.0,
    last_28d_actual: 18855.12
  },
  notes: { narrative: "Projected totals reflect a steady mid-week lift consistent with recent patterns." }
};
