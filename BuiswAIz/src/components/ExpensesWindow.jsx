// src/components/ExpensesWindow.jsx
import React, { useEffect, useState } from "react";
import jsPDF from "jspdf";
import html2canvas from "html2canvas";

const API_BASE = import.meta.env.VITE_API_ASSISTANT_URL;

// Helper for PHP currency
const peso = (n) =>
  new Intl.NumberFormat("en-PH", { style: "currency", currency: "PHP" }).format(
    Number(n ?? 0)
  );

// -------------------- LineChart --------------------
function LineChart({ data = [], width = 400, height = 120 }) {
  if (!Array.isArray(data) || data.length === 0)
    return <div style={{ height }}>No chart data</div>;

  const min = Math.min(...data.map((d) => d.value));
  const max = Math.max(...data.map((d) => d.value));
  const range = max - min || 1;

  const padding = 32;

  // Map points
  const points = data.map((d, i) => {
    const x = padding + (i / (data.length - 1)) * (width - 2 * padding);
    const y = height - padding - ((d.value - min) / range) * (height - 2 * padding);
    return { x, y, label: d.label, value: d.value };
  });

  return (
    <svg width={width} height={height} style={{ background: "#f8fafc", borderRadius: 8 }}>
      {/* Axes */}
      <line x1={padding} y1={height - padding} x2={width - padding} y2={height - padding} stroke="#94a3b8" />
      <line x1={padding} y1={padding} x2={padding} y2={height - padding} stroke="#94a3b8" />

      {/* X axis labels */}
      {points.map((pt, i) => (
        <text key={i} x={pt.x} y={height - padding + 12} textAnchor="middle" fontSize="10" fill="#64748b">
          {pt.label}
        </text>
      ))}

      {/* Y axis labels */}
      {[0, 0.25, 0.5, 0.75, 1].map((frac, i) => {
        const y = height - padding - frac * (height - 2 * padding);
        const val = (min + frac * range).toFixed(0);
        return (
          <g key={i}>
            <line x1={padding - 4} y1={y} x2={padding} y2={y} stroke="#94a3b8" />
            <text x={padding - 6} y={y + 4} textAnchor="end" fontSize="10" fill="#64748b">
              {val}
            </text>
          </g>
        );
      })}

      {/* Line */}
      <polyline
        fill="none"
        stroke="#3b82f6"
        strokeWidth="2"
        points={points.map((pt) => `${pt.x},${pt.y}`).join(" ")}
      />

      {/* Points */}
      {points.map((pt, i) => (
        <circle key={i} cx={pt.x} cy={pt.y} r={3} fill="#3b82f6" />
      ))}
    </svg>
  );
}

// -------------------- ExpensesWindow --------------------
export default function ExpensesWindow({ runId = null, onClose }) {
  const [loading, setLoading] = useState(true);
  const [ui, setUi] = useState(null);
  const [error, setError] = useState(null);
  const [refreshKey, setRefreshKey] = useState(0);

  // -------------------- Fetch Data --------------------
  useEffect(() => {
    let abort = false;

    async function fetchData() {
      try {
        setLoading(true);
        setError(null);

        let uiSpec = null;

        if (runId) {
          const r = await fetch(`${API_BASE}/api/reports/expense/by-id/${runId}`);
          if (!r.ok) throw new Error(`by-id failed: ${r.status}`);
          const j = await r.json();
          uiSpec = j.ui_spec ?? j.uiSpec ?? j.ui ?? null;
        } else {
          const r = await fetch(`${API_BASE}/api/reports/recent?domain=expenses&limit=1`);
          if (!r.ok) throw new Error(`recent failed: ${r.status}`);
          const j = await r.json();
          if (!Array.isArray(j) || j.length === 0) throw new Error("No recent expense reports");
          uiSpec = j[0]?.ui_spec ?? j[0]?.uiSpec ?? j[0]?.ui ?? null;
        }

        if (!abort) setUi(uiSpec);
      } catch (e) {
        if (!abort) setError(String(e?.message || e));
      } finally {
        if (!abort) setLoading(false);
      }
    }

    fetchData();
    return () => {
      abort = true;
    };
  }, [runId, refreshKey]);

  // -------------------- Export PDF --------------------
  const exportPDF = async () => {
    const element = document.querySelector(".sr-root");

    // Hide buttons inside the element before capture
    const buttons = element.querySelectorAll(".sr-actions button");
    buttons.forEach((btn) => (btn.style.display = "none"));

    const canvas = await html2canvas(element, { scale: 2 });
    const imgData = canvas.toDataURL("image/png");

    // Restore buttons after capture
    buttons.forEach((btn) => (btn.style.display = ""));

    // A4 landscape dimensions
    const pdfWidth = 297 * 3.78;
    const pdfHeight = 210 * 3.78;
    const pdf = new jsPDF({
      orientation: "landscape",
      unit: "px",
      format: [pdfWidth, pdfHeight],
    });

    // Scale to fit
    const scaleX = pdfWidth / canvas.width;
    const scaleY = pdfHeight / canvas.height;
    const scale = Math.min(scaleX, scaleY);

    const imgWidth = canvas.width * scale;
    const imgHeight = canvas.height * scale;

    const xOffset = (pdfWidth - imgWidth) / 2;
    const yOffset = (pdfHeight - imgHeight) / 2;

    pdf.addImage(imgData, "PNG", xOffset, yOffset, imgWidth, imgHeight);
    pdf.save("expense-report.pdf");
  };



  // -------------------- UI States --------------------
  if (loading)
    return (
      <div className="pw-card pw-loader">
        <div className="sr-header sr-header--loading">
          <div className="sr-title">Loading Expense Report…</div>
        </div>
      </div>
    );

  if (error)
    return (
      <div className="pw-card">
        <div className="sr-header">
          <h2 className="sr-title">Expense Report</h2>
        </div>
        <div className="sr-text">
          Error: {error}{" "}
          <button className="btn-ghost" onClick={() => setRefreshKey((k) => k + 1)}>
            Retry
          </button>
        </div>
      </div>
    );

  if (!ui) return <div className="pw-card">No data.</div>;

  // -------------------- Parse JSON Spec --------------------
  const title = ui.report_title ?? "Expense Report";
  const period = ui.period?.label ?? "";
  const charts = Array.isArray(ui.charts) ? ui.charts : [];
  const tables = ui.tables || {};
  const narratives = ui.narratives || {};
  const budgetComparison = ui.budget_comparison || null;
  const historicalComparison = ui.historical_comparison || null;

  // Line chart
  let lineChartData = [];
  const lineChart = charts.find((c) => c.type === "line");
  if (lineChart) {
    if (Array.isArray(lineChart.data)) {
      lineChartData = lineChart.data.map((d) => ({
        value: d.value ?? d.y ?? 0,
        label: d.label ?? d.x ?? "",
      }));
    } else if (Array.isArray(lineChart.series) && lineChart.series[0]?.points) {
      lineChartData = lineChart.series[0].points.map((pt) => ({
        value: pt.y ?? pt.value ?? 0,
        label: pt.x ?? pt.label ?? "",
      }));
    }
  }

  // Top categories
  const topCategories = Array.isArray(tables.top_categories)
    ? tables.top_categories.map((row) => ({
        category: row.category ?? row.name ?? "",
        share_pct: row.share_pct ?? 0,
        amount: row.amount ?? 0,
      }))
    : [];

  // Recent transactions
  const recentTx = Array.isArray(tables.recent_transactions)
    ? tables.recent_transactions.map((row) => ({
        date: row.date ?? row.occurred_on ?? "",
        amount: row.amount ?? 0,
      }))
    : [];

  // Summary narrative
  const summaryNarr = Array.isArray(narratives.summary)
    ? narratives.summary.filter(Boolean).join(" ")
    : narratives.summary || "";

  // Recommendations
  const recommendations = Array.isArray(narratives.recommendations)
    ? narratives.recommendations
    : [];
// -------------------- Render --------------------
  return (
    <div className="sr-root">
      {/* Header */}
      <div className="sr-header">
        <div className="sr-header-left">
          <button
            className="sr-back"
            onClick={() => (onClose ? onClose() : window.history.back())}
            aria-label="Close"
            title="Close"
          >
            ←
          </button>
          <h2 className="sr-title">{title}</h2>
        </div>
        {period && (
          <div className="sr-period">
            <i className="sr-period-icon">💸</i>
            <span>{period}</span>
          </div>
        )}
      </div>

      {/* 2-column grid */}
      <div className="sr-grid">
        {/* LEFT COL */}
        <div className="sr-col">
          <div className="sr-card">
            <div className="sr-card-title">Daily Expenses</div>
            {lineChartData.length > 0 ? (
              <LineChart data={lineChartData} />
            ) : (
              <div className="sr-text">No chart data available for this period</div>
            )}
          </div>

          <div className="sr-card">
            <div className="sr-card-title">Top Expense Categories</div>
            {topCategories.length === 0 ? (
              <div className="sr-text">No expense categories found for this period.</div>
            ) : (
              <ul className="sr-list">
                {topCategories.slice(0, 5).map((row, i) => (
                  <li key={i} className="sr-item">
                    <div className="sr-left">
                      <div className="sr-thumb sr-thumb-ph" />
                      <div className="sr-prod">
                        <div className="sr-name">{row.category ?? "—"}</div>
                        <div className="sr-units">{(Number(row.share_pct ?? 0)).toFixed(1)}% of total</div>
                      </div>
                    </div>
                    <div className="sr-right">
                      <span className="sr-rev-label">Amount</span>
                      <span className="sr-rev">{peso(row.amount ?? 0)}</span>
                    </div>
                  </li>
                ))}
              </ul>
            )}
          </div>
        </div>

        {/* RIGHT COL */}
        <div className="sr-col">
          {/* Budget Comparison */}
          {budgetComparison && budgetComparison.allocated_budget > 0 && (
            <div className="sr-card">
              <div className="sr-card-title">Budget Status</div>
              <div style={{ padding: "12px 0" }}>
                <div style={{ display: "flex", justifyContent: "space-between", marginBottom: 8 }}>
                  <span style={{ color: "#64748b" }}>Allocated Budget:</span>
                  <strong>{peso(budgetComparison.allocated_budget)}</strong>
                </div>
                <div style={{ display: "flex", justifyContent: "space-between", marginBottom: 8 }}>
                  <span style={{ color: "#64748b" }}>Total Expenses:</span>
                  <strong>{peso(budgetComparison.total_expenses)}</strong>
                </div>
                <div style={{ display: "flex", justifyContent: "space-between", marginBottom: 8 }}>
                  <span style={{ color: "#64748b" }}>Remaining:</span>
                  <strong style={{ color: budgetComparison.is_over_budget ? "#ef4444" : "#10b981" }}>
                    {peso(budgetComparison.remaining_budget)}
                  </strong>
                </div>
                <div style={{ marginTop: 12 }}>
                  <div style={{ display: "flex", justifyContent: "space-between", marginBottom: 4, fontSize: "0.875rem" }}>
                    <span>Utilization</span>
                    <span>{budgetComparison.utilization_pct?.toFixed(1)}%</span>
                  </div>
                  <div style={{ background: "#e2e8f0", borderRadius: 4, height: 8, overflow: "hidden" }}>
                    <div
                      style={{
                        background: budgetComparison.is_over_budget ? "#ef4444" : "#3b82f6",
                        height: "100%",
                        width: `${Math.min(budgetComparison.utilization_pct, 100)}%`,
                        transition: "width 0.3s ease",
                      }}
                    />
                  </div>
                  {budgetComparison.is_over_budget && (
                    <div style={{ color: "#ef4444", fontSize: "0.875rem", marginTop: 8 }}>⚠️ Over budget!</div>
                  )}
                </div>
              </div>
            </div>
          )}

          {/* Historical Comparison */}
          {historicalComparison && historicalComparison.previous_period_total > 0 && (
            <div className="sr-card">
              <div className="sr-card-title">Period Comparison</div>
              <div style={{ padding: "12px 0" }}>
                <div style={{ display: "flex", justifyContent: "space-between", marginBottom: 8 }}>
                  <span style={{ color: "#64748b" }}>Current Period:</span>
                  <strong>{peso(historicalComparison.current_period_total)}</strong>
                </div>
                <div style={{ display: "flex", justifyContent: "space-between", marginBottom: 8 }}>
                  <span style={{ color: "#64748b" }}>Previous Period:</span>
                  <strong>{peso(historicalComparison.previous_period_total)}</strong>
                </div>
                <div style={{ display: "flex", justifyContent: "space-between", marginBottom: 8 }}>
                  <span style={{ color: "#64748b" }}>Change:</span>
                  <strong style={{ color: historicalComparison.change_amount >= 0 ? "#ef4444" : "#10b981" }}>
                    {historicalComparison.change_amount >= 0 ? "+" : ""}
                    {peso(historicalComparison.change_amount)} (
                    {historicalComparison.change_percentage >= 0 ? "+" : ""}
                    {historicalComparison.change_percentage?.toFixed(1)}%)
                  </strong>
                </div>
                <div style={{ marginTop: 12, padding: "8px 12px", background: "#f8fafc", borderRadius: 4, fontSize: "0.875rem" }}>
                  <strong>Trend:</strong> {historicalComparison.trend || "stable"}
                  {historicalComparison.trend === "increasing" && " 📈"}
                  {historicalComparison.trend === "decreasing" && " 📉"}
                  {historicalComparison.trend === "stable" && " ➡️"}
                </div>
              </div>
            </div>
          )}

          {/* Summary */}
          <div className="sr-card">
            <div className="sr-card-title">Expense Analysis</div>
            <div className="sr-text" style={{ lineHeight: "1.6" }}>
              {summaryNarr || "Generating expense analysis..."}
            </div>
          </div>

          {/* Recommendations */}
          {recommendations.length > 0 && (
            <div className="sr-card">
              <div className="sr-card-title">💡 Recommendations</div>
              <ul style={{ margin: "8px 0", paddingLeft: "20px" }}>
                {recommendations.map((rec, i) => (
                  <li key={i} style={{ marginBottom: 8, lineHeight: "1.5", color: "#475569" }}>
                    {rec}
                  </li>
                ))}
              </ul>
            </div>
          )}

          {/* Recent Transactions */}
          <div className="sr-card">
            <div className="sr-card-title">Recent Transactions</div>
            <div style={{ marginTop: 8 }}>
              <table className="sr-table recent-transactions-table" style={{ width: "100%", borderCollapse: "collapse" }}>
                <thead>
                  <tr>
                    <th style={{ textAlign: "left", padding: "6px 8px" }}>Date</th>
                    <th style={{ textAlign: "right", padding: "6px 8px" }}>Amount</th>
                  </tr>
                </thead>
                <tbody>
                  {recentTx.length === 0 ? (
                    <tr>
                      <td colSpan={2} style={{ textAlign: "center", padding: "10px 0", color: "#888" }}>No recent transactions found.</td>
                    </tr>
                  ) : (
                    recentTx.map((r, i) => (
                      <tr key={i} style={{ borderBottom: "1px solid #f0f0f0" }}>
                        <td style={{ padding: "6px 8px" }}>{r.date ?? "—"}</td>
                        <td style={{ textAlign: "right", padding: "6px 8px" }}>{peso(r.amount ?? 0)}</td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>
          </div>

          {/* Actions */}
           <div className="sr-actions">
            <button className="btn-ghost" onClick={() => setRefreshKey((k) => k + 1)}>
              Regenerate Report
            </button>
            <button className="btn-dark" onClick={exportPDF}>
              Download PDF
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
