// src/tax/TaxCenter.jsx
import React, { useEffect, useMemo, useState } from "react";
import { listTaxedExpensesBetween, monthBounds, yearBounds } from "../api/tax";
import "../stylecss/Dashboard/Dashboard.css";
import "./TaxCenter.css";

function currency(n, min = 2, max = 2) {
  const v = Number(n || 0);
  return v.toLocaleString(undefined, { minimumFractionDigits: min, maximumFractionDigits: max });
}

function clampPct(x) {
  const n = Number(x || 0);
  return Math.max(0, Math.min(100, n));
}


function pct(part = 0, whole = 0) {
  const w = Number(whole || 0);
  const p = Number(part || 0);
  return w > 0 ? (p / w) * 100 : 0;
}

const TYPE_LABEL = {
  PERCENTAGE_TAX: "Percentage Tax",
  VAT: "VAT",
  NONE: "No Tax",
};

export default function TaxCenter({
  triggerLabel = "Tax",
  triggerClass = "btn primary",
}) {
  const [open, setOpen] = useState(false);

  // Filters
  const today = new Date();
  const defaultMonth = `${today.getUTCFullYear()}-${String(today.getUTCMonth() + 1).padStart(2, "0")}`;
  const [mode, setMode] = useState("month"); // 'month' | 'year' | 'custom'
  const [month, setMonth] = useState(defaultMonth);
  const [year, setYear] = useState(String(today.getUTCFullYear()));
  const [customStart, setCustomStart] = useState(new Date(Date.UTC(today.getUTCFullYear(), today.getUTCMonth(), 1)).toISOString().slice(0, 10));
  const [customEnd, setCustomEnd] = useState(new Date(Date.UTC(today.getUTCFullYear(), today.getUTCMonth() + 1, 1)).toISOString().slice(0, 10));
  const [taxFilter, setTaxFilter] = useState("ALL"); // 'ALL' | 'VAT' | 'PERCENTAGE_TAX' | 'NONE'

  

    const [allRows, setAllRows] = useState([]); // all rows for the period (incl. NONE)

  const [loading, setLoading] = useState(false);
  const [rows, setRows] = useState([]);


  

async function load() {
  setLoading(true);
  try {
    let start, end;
    if (mode === "month") ({ start, end } = monthBounds(month));
    else if (mode === "year") ({ start, end } = yearBounds(year));
    else { start = customStart; end = customEnd; }

    const data = await listTaxedExpensesBetween(start, end);

    // 1) Normalize every row to have tax_json.{type,net,tax,gross,...}
    const normalized = (data || []).map(r => {
      // Prefer normalized columns if they exist; otherwise derive from tax_json; otherwise fallback
      const typ = (r.tax_type ?? r.tax_json?.type ?? "NONE");
      const net = Number(r.tax_net ?? r.tax_json?.net ?? r.amount ?? 0);
      const tax = Number(r.tax_tax ?? r.tax_json?.tax ?? 0);
      const gross = Number(r.tax_gross ?? r.tax_json?.gross ?? r.amount ?? 0);
      const withholding = Number(r.tax_withholding ?? r.tax_json?.withholding ?? 0);
      const is_inclusive = (r.tax_is_inclusive ?? r.tax_json?.is_inclusive ?? true);
      const rate = Number(r.tax_rate ?? r.tax_json?.rate ?? 0);
      return {
        ...r,
        tax_json: { type: typ || "NONE", net, tax, gross, withholding, is_inclusive, rate },
      };
    });

    // 2) Save ALL rows for summaries & donuts
    setAllRows(normalized);

    // 3) Apply table filter AFTER normalization
    const tf = String(taxFilter || "ALL").toUpperCase();
    const filtered = tf === "ALL"
      ? normalized
      : normalized.filter(r => String(r.tax_json?.type || "NONE").toUpperCase() === tf);

    setRows(filtered);
  } catch (e) {
    console.error(e);
    setAllRows([]);
    setRows([]);
  } finally {
    setLoading(false);
  }
}


  useEffect(() => { if (open) load(); /* eslint-disable-next-line */ }, [open, mode, month, year, customStart, customEnd, taxFilter]);

  const periodLabel = useMemo(() => {
    if (mode === "month") return new Date(`${month}-01T00:00:00Z`).toLocaleString(undefined, { month: "long", year: "numeric" });
    if (mode === "year")  return `Year ${year}`;
    return `${customStart} → ${customEnd}`;
  }, [mode, month, year, customStart, customEnd]);

  // Summaries
  const summary = useMemo(() => {
    const s = {
      count: rows.length,
      totalGross: 0,
      totalNet: 0,
      totalTax: 0,
      byType: {
        VAT: { count: 0, tax: 0, gross: 0 },
        PERCENTAGE_TAX: { count: 0, tax: 0, gross: 0 },
        NONE: { count: 0, tax: 0, gross: 0 },
      },
    };
    for (const r of rows) {
    const t = r.tax_json || {};
    const typ = t.type || "NONE";

    const gross = Number((t.gross ?? r.amount) || 0);
    const net   = Number((t.net   ?? r.amount) || 0);
    const tax   = Number(t.tax || 0);

    s.totalGross += gross;
    s.totalNet   += net;
    s.totalTax   += tax;

    if (s.byType[typ]) {
        s.byType[typ].count += 1;
        s.byType[typ].tax   += tax;
        s.byType[typ].gross += gross;
    }
    }

    return s;
  }, [rows]);

  function exportCSV() {
    const cols = ["Date", "Type", "Rate", "Inclusive", "Net", "Tax", "Gross", "Withholding", "Category", "Contact", "Notes", "AmountPaid", "ExpenseId"];
    const lines = [cols.join(",")];
    for (const r of rows) {
      const t = r.tax_json || {};
      const vals = [
        r.occurred_on,
        t.type ?? "",
        (typeof t.rate === "number" ? t.rate : ""),
        t.is_inclusive ? "Yes" : "No",
        Number(t.net || 0).toFixed(2),
        Number(t.tax || 0).toFixed(2),
        Number(t.gross || 0).toFixed(2),
        Number(t.withholding || 0).toFixed(2),
        r.category_path ?? "",
        (r.contact_name ?? ""),
        String(r.notes ?? "").replace(/"/g, '""'),
        Number(r.amount || 0).toFixed(2),
        r.id,
      ];
      const escaped = vals.map(v => typeof v === "string" ? `"${v.replace(/"/g, '""')}"` : String(v));
      lines.push(escaped.join(","));
    }
    const blob = new Blob([lines.join("\n")], { type: "text/csv;charset=utf-8;" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `tax-export-${new Date().toISOString().slice(0, 10)}.csv`;
    document.body.appendChild(a);
    a.click();
    a.remove();
    URL.revokeObjectURL(url);
  }

  const taxPct = pct(summary.totalTax, summary.totalGross);

  return (
    <>
      <button className={triggerClass} onClick={() => setOpen(true)}>{triggerLabel}</button>
      {!open ? null : (
        <div
          className="modal-overlay"
          role="dialog"
          aria-modal="true"
          onMouseDown={(e) => { if (e.target === e.currentTarget) setOpen(false); }}
        >
          {/* Modal: capped height + internal scroll */}
          <div
            className="modal modal--tax"
            style={{
              maxWidth: 1100,
              width: "95vw",
              maxHeight: "88vh",
              display: "flex",
              flexDirection: "column",
              padding: 0,
              overflow: "hidden",
            }}
          >
            {/* Header (non-scrolling) */}
            <div
              className="modal__header"
              style={{
                display: "flex",
                alignItems: "center",
                justifyContent: "space-between",
                padding: "12px 16px",
                borderBottom: "1px solid #e5e7eb",
                background: "#fff",
                zIndex: 3
              }}
            >
              <div>
                <h2 style={{ margin: 0, lineHeight: 1.2 }}>Tax Summary</h2>
                <div className="muted" style={{ fontSize: 12, marginTop: 2 }}>
                  Period: <span className="badge" style={{ padding: "2px 8px", borderRadius: 999, background: "#f3f4f6" }}>{periodLabel}</span>
                </div>
              </div>
              <div style={{ display: "flex", gap: 8 }}>
                <button className="btn" onClick={exportCSV}>Export CSV</button>
                <button className="icon-btn" onClick={() => setOpen(false)}>✕</button>
              </div>
            </div>

            {/* Content (scrolls) */}
            <div
              className="modal__content"
              style={{
                flex: "1 1 auto",
                minHeight: 0,
                overflow: "auto",
                padding: 0,
                background: "#fff"
              }}
            >
              {/* Filters (sticky within the content scroller) */}
              <div style={{
                position: "sticky",
                top: 0,
                background: "#fff",
                zIndex: 2,
                borderBottom: "1px solid #e5e7eb"
              }}>
                <div className="fields-grid" style={{ margin: 0, padding: "10px 16px", gridTemplateColumns: "repeat(5, minmax(0,1fr))", gap: 12 }}>
                  <div className="field">
                    <label>Mode</label>
                    <select value={mode} onChange={(e) => setMode(e.target.value)}>
                      <option value="month">Month</option>
                      <option value="year">Year</option>
                      <option value="custom">Custom</option>
                    </select>
                  </div>

                  {mode === "month" && (
                    <div className="field">
                      <label>Month</label>
                      <input type="month" value={month} onChange={(e) => setMonth(e.target.value)} />
                    </div>
                  )}

                  {mode === "year" && (
                    <div className="field">
                      <label>Year</label>
                      <input type="number" value={year} onChange={(e) => setYear(e.target.value)} />
                    </div>
                  )}

                  {mode === "custom" && (
                    <>
                      <div className="field">
                        <label>From</label>
                        <input type="date" value={customStart} onChange={(e) => setCustomStart(e.target.value)} />
                      </div>
                      <div className="field">
                        <label>To</label>
                        <input type="date" value={customEnd} onChange={(e) => setCustomEnd(e.target.value)} />
                      </div>
                    </>
                  )}

                  <div className="field">
                    <label>Tax Type</label>
                    <select value={taxFilter} onChange={(e) => setTaxFilter(e.target.value)}>
                      <option value="ALL">All</option>
                      <option value="PERCENTAGE_TAX">Percentage Tax</option>
                      <option value="VAT">VAT</option>
                      <option value="NONE">No Tax</option>
                    </select>
                  </div>

                  <div className="field" style={{ alignSelf: "end" }}>
                    <button className="btn" onClick={load} disabled={loading}>{loading ? "Loading…" : "Refresh"}</button>
                  </div>
                </div>
              </div>

              {/* ===== PRETTIER KPI DECK ===== */}
              <div className="kpi-deck">
                {/* Total Gross */}
                <div className="kpi-card interactive">
                  <div className="kpi-top">
                    <div className="kpi-icon kpi-icon--gross" aria-hidden>₱</div>
                    <span className="kpi-chip">Gross</span>
                  </div>
                  <div className="kpi-metric">₱ {currency(summary.totalGross)}</div>
                  <div className="kpi-sub">Tax as % of Gross</div>
                 <div className="progress"><span style={{ width: `${clampPct(taxPct)}%` }} /></div>
                  <div className="kpi-foot">
                    <span className="kpi-foot-label">Tax/Gross</span>
                    <span className="kpi-foot-value">{taxPct.toFixed(1)}%</span>
                  </div>
                </div>

                {/* Total Net */}
                <div className="kpi-card interactive">
                  <div className="kpi-top">
                    <div className="kpi-icon kpi-icon--net" aria-hidden>≡</div>
                    <span className="kpi-chip">Net</span>
                  </div>
                  <div className="kpi-metric">₱ {currency(summary.totalNet)}</div>
                  <div className="kpi-sub">Records in period</div>
                  <div className="progress"><span style={{ width: `${clampPct(pct(summary.totalNet, summary.totalGross))}%` }} /></div>
                  <div className="kpi-foot">
                    <span className="kpi-foot-label">Records</span>
                    <span className="kpi-foot-value">{summary.count}</span>
                  </div>
                </div>

                {/* Total Tax */}
                <div className="kpi-card interactive">
                  <div className="kpi-top">
                    <div className="kpi-icon kpi-icon--tax" aria-hidden>%</div>
                    <span className="kpi-chip">Tax</span>
                  </div>
                  <div className="kpi-metric">₱ {currency(summary.totalTax)}</div>
                  <div className="kpi-sub">Avg tax / record</div>
                  <div className="progress"><span style={{ width: `${clampPct(pct(summary.totalTax, summary.totalGross || summary.totalTax || 1))}%` }} /></div>
                  <div className="kpi-foot">
                    <span className="kpi-foot-label">Average</span>
                    <span className="kpi-foot-value">₱ {currency(summary.count ? summary.totalTax / summary.count : 0)}</span>
                  </div>
                </div>

                {/* Mix (donut for VAT share of gross) */}
                <div className="kpi-card interactive">
                  <div className="kpi-top">
                    <div className="kpi-icon kpi-icon--mix" aria-hidden>◎</div>
                    <span className="kpi-chip">Mix</span>
                  </div>
                  <div className="kpi-metric">VAT vs %Tax</div>
                  <div className="kpi-sub">Share of Gross</div>
                  <div className="kpi-mix">
                    <div className="donut" style={{ '--p': pct(summary.byType.VAT.gross, summary.totalGross) }} />
                    <div className="mix-legend">
                      <div><span className="dot dot--vat" /> VAT: {pct(summary.byType.VAT.gross, summary.totalGross).toFixed(1)}%</div>
                      <div><span className="dot dot--ptax" /> %Tax: {pct(summary.byType.PERCENTAGE_TAX.gross, summary.totalGross).toFixed(1)}%</div>
                    </div>
                  </div>
                  <div className="kpi-foot">
                    <span className="kpi-foot-label">Total Gross</span>
                    <span className="kpi-foot-value">₱ {currency(summary.totalGross)}</span>
                  </div>
                </div>
              </div>

              {/* ===== Breakdown by type (with mini donuts) ===== */}
              <div className="breakdown-grid">
                {["PERCENTAGE_TAX", "VAT", "NONE"].map(t => {
                  const b = summary.byType[t];
                  const share = pct(b.gross, summary.totalGross);
                  return (
                    <div className="card small breakdown-card" key={t}>
                      <div className="breakdown-head">
                        <div className="donut sm" style={{ ['--p']: share }} />
                        <div className="breakdown-title">
                          <div className="muted">{TYPE_LABEL[t]}</div>
                          <div className="breakdown-chip">{b.count} rec</div>
                        </div>
                      </div>
                      <div className="breakdown-rows">
                        <div className="row">
                          <span>Gross</span>
                          <strong>₱ {currency(b.gross)}</strong>
                        </div>
                        <div className="row">
                          <span>Tax</span>
                          <strong>₱ {currency(b.tax)}</strong>
                        </div>
                        <div className="row">
                          <span>Share of Gross</span>
                          <strong>{share.toFixed(1)}%</strong>
                        </div>
                      </div>
                      <div className="progress thin">
                        <span style={{ width: `${clampPct(share)}%` }} />
                        </div>
                    </div>
                  );
                })}
              </div>

              {/* ===== Table ===== */}
              <div style={{ borderTop: "1px solid #e5e7eb" }}>
                <table className="table" style={{ minWidth: 900 }}>
                  <thead>
                    <tr>
                      <th>Date</th>
                      <th>Type</th>
                      <th>Rate</th>
                      <th>Incl</th>
                      <th style={{ textAlign: "right" }}>Net</th>
                      <th style={{ textAlign: "right" }}>Tax</th>
                      <th style={{ textAlign: "right" }}>Gross</th>
                      <th style={{ textAlign: "right" }}>Withhold</th>
                      <th>Category</th>
                      <th>Contact</th>
                      <th style={{ width: 240 }}>Notes</th>
                    </tr>
                  </thead>
                  <tbody>
                    {rows.length === 0 && !loading && (
                      <tr>
                        <td colSpan={11} style={{ textAlign: "center", padding: 24, color: "#6b7280" }}>
                          No records for this period/filter.
                        </td>
                      </tr>
                    )}
                    {rows.map(r => {
                    const t = r.tax_json || {};
                    const typ = t.type || "NONE";
                    const net = Number((t.net ?? r.amount) || 0).toFixed(2);
                    const tax = Number(t.tax || 0).toFixed(2);
                    const gross = Number((t.gross ?? r.amount) || 0).toFixed(2);
                    return (
                        <tr key={r.id}>
                        <td style={{ position:"sticky", left:0, background:"#fff" }}>{r.occurred_on}</td>
                        <td>{TYPE_LABEL[typ] || "No Tax"}</td>
                        <td>{Number.isFinite(t.rate) ? t.rate : ""}</td>
                        <td>{t.is_inclusive ? "✓" : ""}</td>
                        <td style={{ textAlign:"right" }}>{net}</td>
                        <td style={{ textAlign:"right" }}>{tax}</td>
                        <td style={{ textAlign:"right" }}>{gross}</td>
                        <td style={{ textAlign:"right" }}>{Number(t.withholding || 0).toFixed(2)}</td>
                        <td>{r.category_path || ""}</td>
                        <td>{r.contact_name || ""}</td>
                        <td title={r.notes || ""} style={{ maxWidth:240, whiteSpace:"nowrap", overflow:"hidden", textOverflow:"ellipsis" }}>
                            {r.notes || ""}
                        </td>
                        </tr>
                    );
                    })}


                  </tbody>
                </table>
              </div>
            </div>
          </div>
        </div>
      )}
    </>
  );
}
