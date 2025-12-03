// src/budget/BudgetHistory.jsx
import { useEffect, useMemo, useState } from "react";
import { listBudgets, getExpensesForBudgetMonth, getBudgetHistory } from "../services/budgetService";
import "./budget-history.css";
import BudgetCategoryPie from "./BudgetCategoryPie";

function currency(n) {
  if (n === null || n === undefined || Number.isNaN(n)) return "₱0.00";
  return `₱${Number(n).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
}

function formatMonth(dateLike) {
  const d = new Date(dateLike);
  return d.toLocaleDateString(undefined, { year: "numeric", month: "long" });
}

function BudgetHistory() {
  const [budgets, setBudgets] = useState([]);
  const [loadingList, setLoadingList] = useState(true);
  const [err, setErr] = useState(null);

  const [open, setOpen] = useState(false);
  const [activeBudget, setActiveBudget] = useState(null);
  const [expenses, setExpenses] = useState([]);
  const [history, setHistory] = useState([]);
  const [loadingDetail, setLoadingDetail] = useState(false);
  const [activeTab, setActiveTab] = useState("expenses");

  useEffect(() => {
    async function run() {
      try {
        setLoadingList(true);
        const data = await listBudgets();
        setBudgets(data);
      } catch (e) {
        console.error(e);
        setErr(e.message || "Failed to load budgets");
      } finally {
        setLoadingList(false);
      }
    }
    run();
  }, []);

  async function openDetail(budget) {
    setActiveBudget(budget);
    setOpen(true);
    setExpenses([]);
    setHistory([]);
    setActiveTab("expenses");
    try {
      setLoadingDetail(true);
      const [exps, hist] = await Promise.all([
        getExpensesForBudgetMonth(budget.month_year),
        getBudgetHistory(budget.id),
      ]);
      setExpenses(exps);
      setHistory(hist);
    } catch (e) {
      console.error(e);
      setErr(e.message || "Failed to load details");
    } finally {
      setLoadingDetail(false);
    }
  }

  const totals = useMemo(() => {
    if (!activeBudget) return null;
    const totalSpend = expenses.reduce((s, r) => s + Number(r.amount || 0), 0);
    const budgetAmt = Number(activeBudget.monthly_budget_amount || 0);
    const remaining = budgetAmt - totalSpend;
    const pct = budgetAmt > 0 ? Math.min(100, Math.max(0, (totalSpend / budgetAmt) * 100)) : (totalSpend > 0 ? 100 : 0);
    const overspent = remaining < 0;
    return { totalSpend, budgetAmt, remaining, pct, overspent };
  }, [activeBudget, expenses]);

  return (
    <div className="bh-wrap">
      <div className="bh-header">
        <h1>Budget History</h1>
        <p className="bh-sub">Browse past monthly budgets and drill into the expenses for each month.</p>
      </div>

      {loadingList && <div className="bh-card">Loading months…</div>}
      {err && <div className="bh-error">{err}</div>}

      {!loadingList && !err && (
        <div className="bh-grid">
          {budgets.map((b) => (
            <button key={b.id} className="bh-card bh-item" onClick={() => openDetail(b)}>
              <div className="bh-row">
                <div className="bh-col">
                  <div className="bh-label">Month</div>
                  <div className="bh-strong">{formatMonth(b.month_year)}</div>
                </div>
                <div className="bh-col">
                  <div className="bh-label">Budget</div>
                  <div className="bh-strong">{currency(b.monthly_budget_amount)}</div>
                </div>
              </div>
              <div className="bh-foot">View details →</div>
            </button>
          ))}
          {budgets.length === 0 && <div className="bh-card">No budgets found.</div>}
        </div>
      )}

      {/* Side panel */}
      {open && activeBudget && (
        <div className="bh-drawer">
          <div className="bh-drawer-header">
            <div>
              <div className="bh-kicker">Budget Month</div>
              <h2>{formatMonth(activeBudget.month_year)}</h2>
            </div>
            <button className="bh-close" onClick={() => setOpen(false)}>✕</button>
          </div>

          <div className="bh-card bh-stats">
            <div className="bh-stat">
              <div className="bh-label">Budget</div>
              <div className="bh-strong">{currency(activeBudget.monthly_budget_amount)}</div>
            </div>
            <div className="bh-stat">
              <div className="bh-label">Total Spent</div>
              <div className="bh-strong">{totals ? currency(totals.totalSpend) : "—"}</div>
            </div>
            <div className={"bh-stat " + (totals?.overspent ? "bh-danger" : "")}>
              <div className="bh-label">{totals?.overspent ? "Overspent" : "Remaining"}</div>
              <div className="bh-strong">{totals ? currency(Math.abs(totals.remaining)) : "—"}</div>
            </div>
          </div>

          <div className="bh-progress">
            <div className="bh-progress-bar" style={{ width: `${totals?.pct ?? 0}%` }} />
          </div>
          <div className="bh-progress-caption">
            {totals ? `${(totals.pct).toFixed(0)}% of budget used` : "—"}
          </div>

          <div className="bh-tabs">
            <button className={activeTab === "expenses" ? "active" : ""} onClick={() => setActiveTab("expenses")}>
              Expenses
            </button>
            <button className={activeTab === "history" ? "active" : ""} onClick={() => setActiveTab("history")}>
              Budget Change Log
            </button>
            <button className={activeTab === "pie" ? "active" : ""} onClick={() => setActiveTab("pie")}>
              Category Breakdown
            </button>
          </div>

          <div className="bh-body">
            {loadingDetail && <div className="bh-card">Loading details…</div>}

            {!loadingDetail && activeTab === "expenses" && (
              <div className="bh-table-wrap">
                {expenses.length === 0 ? (
                  <div className="bh-card">No expenses recorded for this month.</div>
                ) : (
                  <table className="bh-table">
                    <thead>
                      <tr>
                        <th>Date</th>
                        <th>Category</th>
                        <th>Description</th>
                        <th className="bh-right">Amount</th>
                      </tr>
                    </thead>
                    <tbody>
                      {expenses.map((e) => (
                        <tr key={e.id}>
                          <td>{new Date(e.occurred_on).toLocaleDateString()}</td>
                          <td>{e.category_path ?? '—'}</td>
                          <td>{e.notes ?? ''}</td>
                          <td className="bh-right">{currency(e.amount)}</td>
                        </tr>
                      ))}
                    </tbody>
                    <tfoot>
                      <tr>
                        <td colSpan={3} className="bh-right">Total</td>
                        <td className="bh-right">{currency(totals?.totalSpend || 0)}</td>
                      </tr>
                    </tfoot>
                  </table>
                )}
              </div>
            )}

            {!loadingDetail && activeTab === "history" && (
              <div className="bh-table-wrap">
                {history.length === 0 ? (
                  <div className="bh-card">No changes recorded for this month’s budget.</div>
                ) : (
                  <table className="bh-table">
                    <thead>
                      <tr>
                        <th>When</th>
                        <th className="bh-right">Old</th>
                        <th className="bh-right">New</th>
                        <th className="bh-right">Δ Change</th>
                      </tr>
                    </thead>
                    <tbody>
                      {history.map((h) => {
                        const delta = Number(h.new_amount || 0) - Number(h.old_amount || 0);
                        return (
                          <tr key={h.id}>
                            <td>{new Date(h.created_at).toLocaleString()}</td>
                            <td className="bh-right">{currency(h.old_amount)}</td>
                            <td className="bh-right">{currency(h.new_amount)}</td>
                            <td className={"bh-right " + (delta >= 0 ? "bh-pos" : "bh-neg")}>
                              {delta >= 0 ? "+" : ""}
                              {currency(delta)}
                            </td>
                          </tr>
                        );
                      })}
                    </tbody>
                  </table>
                )}
              </div>
            )}



                        {activeTab === "pie" && (
              <BudgetCategoryPie
                expenses={expenses}
                budgetAmount={activeBudget?.monthly_budget_amount}
                monthLabel={formatMonth(activeBudget?.month_year)}
              />
            )}
          </div>
        </div>
      )}
    </div>
  );
}

export default BudgetHistory;