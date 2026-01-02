// src/components/SalesWindow.jsx
import React, { useEffect, useState, useMemo, useRef } from "react";
import jsPDF from "jspdf";
import html2canvas from "html2canvas";

const API_BASE = import.meta.env.VITE_API_ASSISTANT_URL;

/**
 * Props:
 * - runId?: string -> if present, loads that saved run; otherwise loads latest sales report
 */
export default function SalesWindow({ runId = null }) {
  const [loading, setLoading] = useState(true);
  const [ui, setUi] = useState(null);
  const [error, setError] = useState(null);
  const [tooltip, setTooltip] = useState(null);

  const pdfRef = useRef();

  // Fetch report data
  useEffect(() => {
    console.log('🔍 [SalesWindow] useEffect triggered with runId:', runId);
    let abort = false;
    (async () => {
      try {
        setLoading(true);
        setError(null);

        let uiSpec;
        if (runId) {
          console.log('🔍 [SalesWindow] Fetching report by ID:', runId);
          const url = `${API_BASE}/api/reports/sales/by-id/${runId}`;
          console.log('🔍 [SalesWindow] API URL:', url);
          const r = await fetch(url);
          console.log('🔍 [SalesWindow] API Response status:', r.status);
          if (!r.ok) {
            const errorText = await r.text();
            console.error('❌ [SalesWindow] API Error:', r.status, errorText);
            throw new Error(`by-id failed: ${r.status}`);
          }
          const j = await r.json();
          console.log('🔍 [SalesWindow] API Response JSON:', j);
          uiSpec = j.ui_spec ?? j.uiSpec ?? j?.ui ?? null;
          console.log('🔍 [SalesWindow] Extracted uiSpec:', uiSpec);
        } else {
          console.log('🔍 [SalesWindow] Fetching latest report (no runId provided)');
          const url = `${API_BASE}/api/reports/recent?domain=sales&limit=1`;
          console.log('🔍 [SalesWindow] API URL:', url);
          const r = await fetch(url);
          console.log('🔍 [SalesWindow] API Response status:', r.status);
          if (!r.ok) {
            const errorText = await r.text();
            console.error('❌ [SalesWindow] API Error:', r.status, errorText);
            throw new Error(`recent failed: ${r.status}`);
          }
          const j = await r.json();
          console.log('🔍 [SalesWindow] API Response JSON:', j);
          if (!Array.isArray(j) || j.length === 0) {
            console.warn('⚠️ [SalesWindow] No recent reports found');
            throw new Error("No recent reports");
          }
          uiSpec = j[0]?.ui_spec ?? j[0]?.uiSpec ?? j[0]?.ui ?? null;
          console.log('🔍 [SalesWindow] Extracted uiSpec from recent:', uiSpec);
        }

        if (!abort) {
          console.log('✅ [SalesWindow] Setting UI state with uiSpec');
          setUi(uiSpec);
        }
      } catch (e) {
        console.error('❌ [SalesWindow] Error caught:', e);
        if (!abort) setError(String(e?.message || e));
      } finally {
        if (!abort) {
          console.log('🔍 [SalesWindow] Finished loading');
          setLoading(false);
        }
      }
    })();
    return () => { abort = true; };
  }, [runId]);

  // Prepare chart data with actual day numbers on X-axis
  const chartData = useMemo(() => {
    console.log('🔍 [SalesWindow] chartData useMemo triggered');
    console.log('🔍 [SalesWindow] ui object:', ui);
    
    if (!ui) {
      console.warn('⚠️ [SalesWindow] No ui object');
      return null;
    }
    
    if (!Array.isArray(ui.charts)) {
      console.warn('⚠️ [SalesWindow] ui.charts is not an array:', ui.charts);
      return null;
    }
    
    if (!ui.charts[0]) {
      console.warn('⚠️ [SalesWindow] ui.charts[0] is missing');
      return null;
    }
    
    if (!Array.isArray(ui.charts[0].series)) {
      console.warn('⚠️ [SalesWindow] ui.charts[0].series is not an array:', ui.charts[0].series);
      return null;
    }
    
    if (!ui.charts[0].series[0]) {
      console.warn('⚠️ [SalesWindow] ui.charts[0].series[0] is missing');
      return null;
    }
    
    if (!Array.isArray(ui.charts[0].series[0].points)) {
      console.warn('⚠️ [SalesWindow] ui.charts[0].series[0].points is not an array:', ui.charts[0].series[0].points);
      return null;
    }
    
    console.log('🔍 [SalesWindow] Chart points:', ui.charts[0].series[0].points);
    
    try {
      const rawPoints = ui.charts[0].series[0].points.map((pt, idx) => {
        console.log(`🔍 [SalesWindow] Processing point ${idx}:`, pt);
        
        if (!pt.x || typeof pt.x !== 'string') {
          console.warn(`⚠️ [SalesWindow] Invalid pt.x at index ${idx}:`, pt.x);
          return { day: idx + 1, y: Number(pt.y ?? 0) };
        }
        
        const parts = pt.x.split("-");
        if (parts.length < 3) {
          console.warn(`⚠️ [SalesWindow] Invalid date format at index ${idx}: ${pt.x}`);
          return { day: idx + 1, y: Number(pt.y ?? 0) };
        }
        
        const day = Number(parts[2]);
        if (isNaN(day)) {
          console.warn(`⚠️ [SalesWindow] Invalid day number at index ${idx}: ${parts[2]}`);
          return { day: idx + 1, y: Number(pt.y ?? 0) };
        }
        
        return {
          day: day,
          y: Number(pt.y ?? 0)
        };
      });
      
      console.log('🔍 [SalesWindow] Parsed rawPoints:', rawPoints);
      
      if (rawPoints.length === 0) {
        console.warn('⚠️ [SalesWindow] No points after parsing');
        return null;
      }

      const w = 600, h = 300, pad = 50;
      const allY = rawPoints.map(p => p.y);
      const minY = Math.min(...allY);
      const maxY = Math.max(...allY);

      const minX = Math.min(...rawPoints.map(p => p.day));
      const maxX = Math.max(...rawPoints.map(p => p.day));
      
      console.log('🔍 [SalesWindow] Chart dimensions:', { minX, maxX, minY, maxY, pointCount: rawPoints.length });

      const xCoord = (day) => pad + ((day - minX) / (maxX - minX || 1)) * (w - pad * 2);
      const yCoord = (y) => h - pad - ((y - minY) / (maxY - minY || 1)) * (h - pad * 2);

      const pathD = rawPoints.map((pt, i) => {
        const x = xCoord(pt.day);
        const y = yCoord(pt.y);
        return `${i === 0 ? "M" : "L"} ${x} ${y}`;
      }).join(" ");

      console.log('✅ [SalesWindow] Chart data generated successfully');
      
      return {
        width: w, height: h, padding: pad,
        pathD, rawPoints, minX, maxX, minY, maxY,
        xCoord, yCoord
      };
    } catch (err) {
      console.error('❌ [SalesWindow] Error parsing chart data:', err);
      return null;
    }
  }, [ui]);

  const downloadPDF = async () => {
    if (!pdfRef.current) return;
    const canvas = await html2canvas(pdfRef.current, { scale: 2 });
    const imgData = canvas.toDataURL("image/png");
    const pdf = new jsPDF({ orientation: "landscape", unit: "px", format: [canvas.width, canvas.height] });
    pdf.addImage(imgData, "PNG", 0, 0, canvas.width, canvas.height);
    pdf.save("SalesReport.pdf");
  };

  if (loading) return <div className="pw-card pw-loader">Loading…</div>;
  if (error) return <div className="pw-card">Error: {error}</div>;
  if (!ui) {
    console.error('❌ [SalesWindow] No UI data available for rendering');
    return <div className="pw-card">No data.</div>;
  }

  console.log('✅ [SalesWindow] Rendering with UI data:', ui);

  const title = ui.report_title ?? "Sales Report";
  const period = ui.period?.label ?? "";
  const narratives = ui.narratives ?? {};
  const performanceTxt = narratives.performance || "";
  const trendsTxt = narratives.trends || "";
  const tipTxt = narratives.best_sellers_tips || "";
  const cards = Array.isArray(ui.cards) ? ui.cards : [];
  const topItems = (cards[0]?.items ?? []).slice(0, 3);

  const peso = (n) =>
    new Intl.NumberFormat("en-PH", { style: "currency", currency: "PHP" }).format(n ?? 0);

  return (
    <div className="sr-root">
      {/* Report content for PDF */}
      <div ref={pdfRef}>
        <div className="sr-header">
          <h2 className="sr-title">{title}</h2>
          {period && <div className="sr-period">{period}</div>}
        </div>

        <div className="sr-grid">
          <div className="sr-col">
            <div className="sr-card">
              <div className="sr-card-title">Sales Performance Overview</div>
              <p className="sr-text">{performanceTxt || "No performance narrative available."}</p>
            </div>

            <div className="sr-card">
              <div className="sr-card-title">Best-Selling Products</div>
              {tipTxt && <div className="sr-sub">{tipTxt}</div>}
              <ul className="sr-list">
                {topItems.length === 0 && <li className="sr-item">No products.</li>}
                {topItems.map((p, i) => (
                  <li key={i} className="sr-item">
                    <div className="sr-left">
                      <div className="sr-prod">
                        <div className="sr-name">{p.product}</div>
                        <div className="sr-units">{Number(p.units ?? 0)} pcs sold</div>
                      </div>
                    </div>
                    <div className="sr-right">
                      <span className="sr-rev-label">Revenue</span>
                      <span className="sr-rev">{peso(p.amount ?? 0)}</span>
                      <span className="sr-share">{(Number(p.share_pct ?? 0)).toFixed(1)}% of sales</span>
                    </div>
                  </li>
                ))}
              </ul>
            </div>
          </div>

          <div className="sr-col">
            <div className="sr-card">
              <div className="sr-card-title">Sales Trends</div>
              <p className="sr-text">{trendsTxt || "Trend details unavailable."}</p>
            </div>

            <div className="sr-card sr-chart">
              {!chartData ? (
                <div style={{ padding: '40px', textAlign: 'center', color: '#999' }}>
                  <p>⚠️ Chart data unavailable</p>
                  <p style={{ fontSize: '12px', marginTop: '10px' }}>
                    Check console logs for details
                  </p>
                </div>
              ) : (
                <svg width={chartData.width} height={chartData.height} viewBox={`0 0 ${chartData.width} ${chartData.height}`}>
                  {/* Y-axis labels */}
                  {[0, 0.25, 0.5, 0.75, 1].map(f => {
                    const y = chartData.height - chartData.padding - f * (chartData.height - chartData.padding * 2);
                    const val = Math.round(chartData.minY + f * (chartData.maxY - chartData.minY));
                    return (
                      <g key={f}>
                        <line x1={chartData.padding} y1={y} x2={chartData.width - chartData.padding} y2={y} stroke="#e0e0e0" />
                        <text x={chartData.padding - 10} y={y + 4} textAnchor="end" fontSize="10">{val}</text>
                      </g>
                    );
                  })}

                  {/* X-axis labels */}
                  {chartData.rawPoints.map((pt, i) => {
                    const x = chartData.xCoord(pt.day);
                    return (
                      <g key={i}>
                        <line x1={x} y1={chartData.height - chartData.padding} x2={x} y2={chartData.height - chartData.padding + 5} stroke="#000" />
                        <text x={x} y={chartData.height - chartData.padding + 15} textAnchor="middle" fontSize="10">{pt.day}</text>
                      </g>
                    );
                  })}

                {/* Line path */}
                <path d={chartData.pathD} fill="none" stroke="#2563eb" strokeWidth="2" />

                {/* Points with hover tooltip */}
                {chartData.rawPoints.map((pt, i) => {
                  const x = chartData.xCoord(pt.day);
                  const y = chartData.yCoord(pt.y);
                  return (
                    <circle
                      key={i}
                      cx={x}
                      cy={y}
                      r={4}
                      fill="#2563eb"
                      onMouseEnter={(e) => setTooltip({ x: e.clientX, y: e.clientY, value: pt.y, day: pt.day })}
                      onMouseLeave={() => setTooltip(null)}
                    >
                      <title>{`Day ${pt.day}: ${pt.y}`}</title>
                    </circle>
                  );
                })}
              </svg>
              )}

              {/* Tooltip overlay */}
              {tooltip && (
                <div
                  style={{
                    position: "fixed",
                    left: tooltip.x + 10,
                    top: tooltip.y + 10,
                    background: "#333",
                    color: "#fff",
                    padding: "4px 8px",
                    borderRadius: 4,
                    pointerEvents: "none",
                    fontSize: 12,
                  }}
                >
                  Day {tooltip.day}: {tooltip.value}
                </div>
              )}
            </div>
          </div>
        </div>
      </div>

      {/* Buttons outside PDF */}
      <div className="sr-actions">
        <button className="btn-ghost" onClick={() => alert("Regenerate coming soon")}>Regenerate Report</button>
        <button className="btn-dark" onClick={downloadPDF}>Download PDF</button>
      </div>
    </div>
  );
}