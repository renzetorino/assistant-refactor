// src/budget/BudgetCenter.jsx
import React, { useEffect, useMemo, useRef, useState } from "react";
import { supabase } from "../supabase";
import { listExpensesByMonth } from "../api/expenses";
import {
  listBudgets,
  getBudgetHistory,
  upsertBudget as _unusedUpsertBudget,
  updateBudgetById,
} from "../services/budgetService";
import BudgetCategoryPie from "./BudgetCategoryPie";
import "./budget-history.css";
import "../stylecss/Dashboard/Dashboard.css";

/* ---------- small helpers ---------- */
function currency(n, min = 2, max = 2) {
  if (n === null || n === undefined || Number.isNaN(n)) return "₱0.00";
  return `₱${Number(n).toLocaleString(undefined, { minimumFractionDigits: min, maximumFractionDigits: max })}`;
}
function fmtMonthLabel(isoDate) {
  if (!isoDate) return "";
  const d = new Date(isoDate);
  return d.toLocaleDateString(undefined, { year: "numeric", month: "long" });
}
function ymFromISO(iso) {
  const [y, m] = iso.split("-").map(Number);
  return { yyyy: y, mm: m };
}

/* ---------- lightweight toast system (no deps) ---------- */
function useToasts() {
  const [toasts, setToasts] = useState([]);
  const idRef = useRef(1);

  function push(message, type = "success", ttl = 3000) {
    const id = idRef.current++;
    setToasts((t) => [...t, { id, message, type }]);
    setTimeout(() => {
      setToasts((t) => t.filter((x) => x.id !== id));
    }, ttl);
  }

  const api = {
    success: (m, ttl) => push(m, "success", ttl),
    error: (m, ttl) => push(m, "error", ttl),
    info: (m, ttl) => push(m, "info", ttl),
  };

  return [toasts, api];
}

/* ---------- component ---------- */
export default function BudgetCenter({
  triggerLabel = "Budget",
  triggerClass = "btn primary", // you can override from expenses.jsx
  /**
   * Optional: parent can pass an opener for the Expense edit modal.
   * If provided, clicking a row in the Expenses tab will call this.
   * onOpenExpense(rowOrId: Expense | string)
   */
  onOpenExpense,
}) {
  const [open, setOpen] = useState(false);

  // list & selection
  const [budgets, setBudgets] = useState([]);
  const [loading, setLoading] = useState(false);
  const [active, setActive] = useState(null);
  const [tab, setTab] = useState("expenses");

  // add budget controls
  const [addOpen, setAddOpen] = useState(false);
  const [addMonth, setAddMonth] = useState(""); // YYYY-MM from <input type="month">
  const [addAmount, setAddAmount] = useState("");

  // edit budget controls
  const [editOpen, setEditOpen] = useState(false);
  const [editingBudget, setEditingBudget] = useState(null);
  const [editAmount, setEditAmount] = useState("");

  // detail data
  const [detailLoading, setDetailLoading] = useState(false);
  const [expenses, setExpenses] = useState([]);
  const [history, setHistory] = useState([]);
  const [err, setErr] = useState("");

  // toasts
  const [toasts, toast] = useToasts();

  // Year filter for budgets
  const currentYear = new Date().getFullYear();
  const years = useMemo(() => {
    const ys = Array.from(
      new Set(
        (budgets || [])
          .map((b) => {
            const d = new Date(b.month_year);
            return Number.isFinite(d.getFullYear()) ? d.getFullYear() : null;
          })
          .filter(Boolean)
      )
    );
    ys.sort((a, b) => b - a); // newest year first
    return ys;
  }, [budgets]);

  const [yearFilter, setYearFilter] = useState(() =>
    years.includes(currentYear) ? currentYear : years[0] ?? "All"
  );

  useEffect(() => {
    // Keep selection valid when budgets change
    if (!years.length) {
      setYearFilter("All");
    } else if (yearFilter !== "All" && !years.includes(yearFilter)) {
      setYearFilter(years[0]);
    }
  }, [years]);

  const filteredBudgets = useMemo(() => {
    if (yearFilter === "All") return budgets;
    return (budgets || []).filter((b) => {
      const d = new Date(b.month_year);
      return d.getFullYear() === yearFilter;
    });
  }, [budgets, yearFilter]);

  async function refreshBudgets() {
    setLoading(true);
    try {
      const rows = await listBudgets();
      setBudgets(rows);
      if (active) {
        const found = rows.find((b) => b.id === active.id);
        setActive(found || null);
      }
    } catch (e) {
      setErr(e.message || "Failed to load budgets");
      toast.error("Failed to load budgets");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    if (open) refreshBudgets();
  }, [open]);

  async function loadDetail(budget) {
    if (!budget) return;
    setDetailLoading(true);
    setErr("");
    try {
      const { yyyy, mm } = ymFromISO(budget.month_year);
      const exps = await listExpensesByMonth(yyyy, mm);
      const hist = await getBudgetHistory(budget.id);
      setExpenses(exps);
      setHistory(hist);
    } catch (e) {
      setErr(e.message || "Failed to load details");
      toast.error("Failed to load details");
    } finally {
      setDetailLoading(false);
    }
  }

  useEffect(() => {
    if (active) loadDetail(active);
  }, [active]);

  async function upsertBudget(monthYYYYMM, amount) {
    const iso = `${monthYYYYMM}-01`;
    const { error } = await supabase
      .from("budget")
      .upsert({ month_year: iso, monthly_budget_amount: Number(amount || 0) }, { onConflict: ["month_year"] });
    if (error) throw error;
  }

  async function onAddSubmit(e) {
    e.preventDefault();
    try {
      if (!addMonth) throw new Error("Select a month");
      await upsertBudget(addMonth, addAmount || 0); // trigger logs
      setAddOpen(false);
      setAddMonth("");
      setAddAmount("");
      await refreshBudgets();
      toast.success("Budget added");
    } catch (e2) {
      toast.error(e2.message || "Failed to add budget");
    }
  }

  function openEdit(b) {
    setEditingBudget(b);
    setEditOpen(true);
    setEditAmount(String(b.monthly_budget_amount ?? ""));
  }

  async function onEditSubmit(e) {
    e.preventDefault();
    if (!editingBudget) return;
    try {
      await updateBudgetById(editingBudget.id, editAmount || 0); // trigger logs
      setEditOpen(false);
      setEditingBudget(null);
      await refreshBudgets();
      toast.success("Budget updated");
    } catch (e2) {
      toast.error(e2.message || "Failed to update budget");
    }
  }

  const totals = useMemo(() => {
    const used = expenses.reduce((s, e) => s + Number(e.amount || 0), 0);
    const budgetAmt = Number(active?.monthly_budget_amount || 0);
    return { used, budgetAmt, remaining: Math.max(0, budgetAmt - used), count: expenses.length };
  }, [expenses, active]);

  /* ---------- NEW: open record in parent (edit) ---------- */
  async function openRecordInParent(row) {
    if (!onOpenExpense) return;
    try {
      const { data, error } = await supabase
        .from("expenses")
        .select("id, occurred_on, amount, notes, status, contact_id, category_id")
        .eq("id", row.id)
        .maybeSingle();
      if (error) throw error;
      onOpenExpense(data || row);
      setOpen(false); // optional: close after handing off
    } catch (e) {
      console.error(e);
      onOpenExpense(row.id); // at least pass the id
      setOpen(false);
    }
  }

  /* ---------- NEW: CSV export for month expenses ---------- */
  function exportMonthCSV() {
    if (!active) return;
    const header = ["Date", "Category", "Notes", "Amount"];
    const lines = [header.join(",")];
    for (const r of expenses) {
      const row = [
        r.occurred_on ? new Date(r.occurred_on).toLocaleDateString() : "",
        r.category_path || "Uncategorized",
        (r.notes || "").replaceAll('"', '""'),
        Number(r.amount || 0).toFixed(2),
      ];
      lines.push(row.map((v) => `"${String(v)}"`).join(","));
    }
    const csv = lines.join("\n");
    const blob = new Blob([csv], { type: "text/csv;charset=utf-8;" });
    const url = URL.createObjectURL(blob);
    const nameSafe = (fmtMonthLabel(active.month_year) || "month").replace(/[^a-z0-9-_]+/gi, "_");
    const a = document.createElement("a");
    a.href = url;
    a.download = `expenses_${nameSafe}_${new Date().toISOString().slice(0, 10)}.csv`;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
  }

  return (
    <>
      {/* Trigger button (styled via props) */}
      <button className={triggerClass} onClick={() => setOpen(true)}>
        {triggerLabel}
      </button>

      {!open ? null : (

        <div className="modal-overlay" style={{ zIndex: 1000 }} onMouseDown={(e) => { if (e.target === e.currentTarget) setOpen(false); }}>
          <div className="modal" style={{ width: "min(980px, 94vw)", maxHeight: "90vh", overflow: "hidden", display: "flex", flexDirection: "column" }}>
            
            <div className="header-bar modal-header" style={{ zIndex: 1001 }}>
              <h2 style={{ margin: 0 }}>Budgets</h2>
              <button className="btn icon" onClick={() => setOpen(false)} aria-label="Close" style={{ position: "relative", zIndex: 1002, cursor: "pointer" }}>✕</button>
            </div>

            <div style={{ overflow: "auto", flex: 1, padding: "0 20px 20px 20px" }}>
            {/* Controls row: Year (left) + Add budget (right) */}
            <div
              className="controls-row"
              style={{
                display: "flex",
                alignItems: "center",
                justifyContent: "space-between",
                gap: 12,
                flexWrap: "wrap",
                marginTop: 8,
                marginBottom: 8,
              }}
            >
              {/* Left: Year filter */}
              <div className="bh-filter" style={{ display: "flex", gap: 8, alignItems: "center" }}>
                <label htmlFor="year-filter" className="muted" style={{ fontSize: 12 }}>
                  Year
                </label>
                <select
                  id="year-filter"
                  className="input nice-select"
                  value={yearFilter}
                  onChange={(e) =>
                    setYearFilter(e.target.value === "All" ? "All" : Number(e.target.value))
                  }
                  style={{ padding: "6px 8px", borderRadius: 8, border: "1px solid #e5e7eb" }}
                >
                  <option value="All">All</option>
                  {years.map((y) => (
                    <option key={y} value={y}>
                      {y}
                    </option>
                  ))}
                </select>
              </div>

              {/* Right: Add budget / form */}
              <div className="add-budget-bar" style={{ marginLeft: "auto" }}>
                {!addOpen ? (
                  <button className="btn primary" onClick={() => setAddOpen(true)}>
                    + Add budget
                  </button>
                ) : (
                  <form className="add-budget-form inline-card compact-row" onSubmit={onAddSubmit}>
                    <label className="stack">
                      <span className="muted">Month</span>
                      <input
                        className="input"
                        type="month"
                        value={addMonth}
                        onChange={(e) => setAddMonth(e.target.value)}
                        required
                      />
                    </label>

                    <label className="stack">
                      <span className="muted">Amount</span>
                      <div className="input-affix">
                        <span className="affix">₱</span>
                        <input
                          type="number"
                          min="0"
                          step="0.01"
                          value={addAmount}
                          onChange={(e) => setAddAmount(e.target.value)}
                          required
                        />
                      </div>
                    </label>

                    <div className="actions">
                      <button type="submit" className="btn primary">
                        Save
                      </button>
                      <button
                        type="button"
                        className="btn outline"
                        onClick={() => {
                          setAddOpen(false);
                          setAddMonth("");
                          setAddAmount("");
                        }}
                      >
                        Cancel
                      </button>
                    </div>
                  </form>
                )}
              </div>
            </div>

            {/* List of months with budgets */}
            <div className="history-wrap" style={{ marginBottom: 16 }}>
              {loading ? (
                <p>Loading budgets…</p>
              ) : (
                <div className="log-scroll">
                  <div className="bh-grid scrollable">
                    {filteredBudgets.map((b) => (
                      <div
                        key={b.id}
                        className="bh-card bh-item"
                        role="button"
                        tabIndex={0}
                        onClick={() => {
                          setActive(b);
                          setTab("expenses");
                        }}
                        onKeyDown={(e) => {
                          if (e.key === "Enter" || e.key === " ") {
                            e.preventDefault();
                            setActive(b);
                            setTab("expenses");
                          }
                        }}
                        title="Open"
                      >
                        <div className="bh-row">
                          <div className="bh-col">
                            <div className="bh-label">Month</div>
                            <div className="bh-strong">{fmtMonthLabel(b.month_year)}</div>
                          </div>
                          <div className="bh-col" style={{ textAlign: "right" }}>
                            <div className="bh-label">Budget</div>
                            <div className="bh-strong">{currency(b.monthly_budget_amount)}</div>
                          </div>
                        </div>

                        <div className="bh-foot">
                          <button
                            onClick={(e) => {
                              e.stopPropagation();
                              openEdit(b);
                            }}
                            className="btn primary"
                            title="Edit"
                          >
                            Edit
                          </button>
                        </div>
                      </div>
                    ))}
                    {!filteredBudgets.length && (
                      <div className="bh-card">
                        <div className="muted">No budgets yet. Add one above.</div>
                      </div>
                    )}
                  </div>
                </div>
              )}
            </div>

            {/* Detail for selected month */}
            {active && (
              <div className="history-wrap">
                <div className="budget-detail-header">
                  <h3 style={{ margin: 0 }}>{fmtMonthLabel(active.month_year)}</h3>
                  <div className="muted totals-inline">
                    <span>
                      Budget: <strong>{currency(active.monthly_budget_amount)}</strong>
                    </span>
                    <span>
                      Used: <strong>{currency(totals.used)}</strong>
                    </span>
                    <span>
                      Remaining: <strong>{currency(totals.remaining)}</strong>
                    </span>
                  </div>
                </div>

                {/* Tabs */}
                <div className="tabs" style={{ marginTop: 10 }}>
                  <button
                    className={tab === "expenses" ? "active" : ""}
                    onClick={() => setTab("expenses")}
                  >
                    Expenses
                  </button>
                  <button className={tab === "log" ? "active" : ""} onClick={() => setTab("log")}>
                    Budget change log
                  </button>
                  <button className={tab === "pie" ? "active" : ""} onClick={() => setTab("pie")}>
                    Category Breakdown
                  </button>
                </div>

                <div className="tab-body" style={{ marginTop: 12 }}>
                  {detailLoading && <p>Loading…</p>}

                  {!detailLoading && tab === "expenses" && (
                    <div className="table-wrap">
                      {/* NEW: summary + Export */}
                      <div
                        className="records-summary muted"
                        style={{ display: "flex", gap: 16, alignItems: "center", marginBottom: 8 }}
                      >
                        <span>
                          Total records: <strong>{totals.count}</strong>
                        </span>
                        <span>
                          Sum: <strong>{currency(totals.used)}</strong>
                        </span>
                        <div style={{ marginLeft: "auto" }}>
                          <button className="btn xs" onClick={exportMonthCSV}>
                            Export CSV
                          </button>
                        </div>
                      </div>
                      <div className="logs-scroll">
                        <table>
                          <thead>
                            <tr>
                              <th style={{ textAlign: "left" }}>Date</th>
                              <th style={{ textAlign: "left" }}>Category</th>
                              <th style={{ textAlign: "left" }}>Notes</th>
                              <th style={{ textAlign: "right" }}>Amount</th>
                            </tr>
                          </thead>
                          <tbody>
                            {expenses.map((e) => (
                              <tr
                                key={e.id}
                                className={onOpenExpense ? "clickable-row" : ""}
                                style={{ cursor: onOpenExpense ? "pointer" : "default" }}
                                onClick={() => onOpenExpense && openRecordInParent(e)}
                                title={onOpenExpense ? "Open in editor" : ""}
                              >
                                <td>{new Date(e.occurred_on).toLocaleDateString()}</td>
                                <td>{e.category_path || "Uncategorized"}</td>
                                <td>{e.notes || ""}</td>
                                <td style={{ textAlign: "right" }}>{currency(e.amount)}</td>
                              </tr>
                            ))}
                            {!expenses.length && (
                              <tr>
                                <td colSpan="4" className="muted">
                                  No expenses for this month.
                                </td>
                              </tr>
                            )}
                          </tbody>
                        </table>
                      </div>
                    </div>
                  )}

                  {!detailLoading && tab === "log" && (
                    <div className="table-wrap">
                      <table>
                        <thead>
                          <tr>
                            <th style={{ textAlign: "left" }}>When</th>
                            <th style={{ textAlign: "right" }}>Old</th>
                            <th style={{ textAlign: "right" }}>New</th>
                          </tr>
                        </thead>
                        <tbody>
                          {history.map((h) => (
                            <tr key={h.id}>
                              <td>{new Date(h.created_at).toLocaleString()}</td>
                              <td style={{ textAlign: "right" }}>{currency(h.old_amount)}</td>
                              <td style={{ textAlign: "right" }}>{currency(h.new_amount)}</td>
                            </tr>
                          ))}
                          {!history.length && (
                            <tr>
                              <td colSpan="3" className="muted">
                                No budget changes yet.
                              </td>
                            </tr>
                          )}
                        </tbody>
                      </table>
                    </div>
                  )}

                  {!detailLoading && tab === "pie" && (
                    <BudgetCategoryPie
                      expenses={expenses}
                      budgetAmount={active?.monthly_budget_amount}
                      monthLabel={fmtMonthLabel(active?.month_year)}
                    />
                  )}
                </div>
              </div>
            )}
            </div>
          </div>
        </div>
      )}

      {/* Edit Budget modal */}
      {editOpen && (
        <div
          className="modal-overlay"
          onMouseDown={(e) => {
            if (e.target === e.currentTarget) setEditOpen(false);
          }}
          style={{ zIndex: 1100 }}
        >
          <form
            className="modal"
            onMouseDown={(e) => e.stopPropagation()}
            onSubmit={onEditSubmit}
            style={{ position: "relative", zIndex: 1110 }}
          >
            <div
              className="modal-header"
              style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}
            >
              <h3 style={{ margin: 0 }}>Edit Budget</h3>
              <button
                type="button"
                className="btn icon"
                aria-label="Close"
                onMouseDown={(e) => e.stopPropagation()}
                onClick={(e) => {
                  e.stopPropagation();
                  setEditOpen(false);
                }}
              >
                ✕
              </button>
            </div>
            <div className="input-affix">
              <span className="affix">₱</span>
              <input
                type="number"
                min="0"
                step="0.01"
                value={editAmount}
                onChange={(e) => setEditAmount(e.target.value)}
                required
              />
            </div>
            <div className="modal-footer" style={{ display: "flex", justifyContent: "flex-end", gap: 8 }}>
              <button
                type="button"
                className="btn outline"
                onMouseDown={(e) => e.stopPropagation()}
                onClick={(e) => {
                  e.stopPropagation();
                  setEditOpen(false);
                }}
              >
                Cancel
              </button>
              <button type="submit" className="btn primary">
                Save changes
              </button>
            </div>
          </form>
        </div>
      )}

      {/* Toasts */}
      <div className="toast-stack">
        {toasts.map((t) => (
          <div key={t.id} className={`toast ${t.type}`}>
            {t.message}
          </div>
        ))}
      </div>
    </>
  );
}
