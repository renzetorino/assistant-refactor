// src/budget/BudgetCategoryPie.jsx
import React, { useMemo } from "react";
import {
  PieChart,
  Pie,
  Tooltip,
  Legend,
  Cell,
  ResponsiveContainer,
  Label,
} from "recharts";

/** CONFIG — tweak to taste */
const MAX_LEGEND_ITEMS = 50;        // cap legend entries; rest go into "Other"
const OTHER_MIN_PCT_OF_BUDGET = 0.02; // items <2% of budget go to "Other"
const COLORS = [
  "#0088FE", "#00C49F", "#FFBB28", "#FF8042", "#A28BFF",
  "#FF6699", "#66CC66", "#9966FF", "#66CCCC", "#FFCC66",
  "#CC8899", "#8EC07C", "#B16286", "#458588", "#D79921", "#689d6a",
];

/** Custom wrapping + scrollable legend */
function CustomLegend({ payload = [] }) {
  return (
    <div className="pie-legend" role="list">
      {payload.map((entry, idx) => (
        <div className="pie-legend-item" role="listitem" key={idx} title={entry.value}>
          <span className="pie-legend-swatch" style={{ background: entry.color }} />
          <span className="pie-legend-text">{entry.value}</span>
        </div>
      ))}
    </div>
  );
}

export default function BudgetCategoryPie({
  expenses = [],
  budgetAmount = 0,
  monthLabel = "",
}) {
  const fmtCurrency = (n, min = 2, max = 2) =>
    `₱${Number(n).toLocaleString(undefined, {
      minimumFractionDigits: min,
      maximumFractionDigits: max,
    })}`;

  const {
    chartData, // includes "Remaining" if any
    totalUsed,
    remaining,
  } = useMemo(() => {
    // Group by category
    const byCat = new Map();
    let used = 0;
    for (const e of expenses) {
      const key = e?.category_path || "Uncategorized";
      const amt = Number(e?.amount || 0);
      if (!amt) continue;
      used += amt;
      byCat.set(key, (byCat.get(key) || 0) + amt);
    }

    const budget = Number(budgetAmount || 0);
    const rows = Array.from(byCat, ([name, value]) => ({ name, value }));

    // Sort largest first
    rows.sort((a, b) => b.value - a.value);

    // Aggregate small ones into "Other" by % of budget OR if exceeding max legend items
    const threshold = budget > 0 ? budget * OTHER_MIN_PCT_OF_BUDGET : 0;
    const main = [];
    const others = [];

    rows.forEach((r, idx) => {
      if ((threshold && r.value < threshold) || idx >= MAX_LEGEND_ITEMS - 1) {
        others.push(r);
      } else {
        main.push(r);
      }
    });

    if (others.length) {
      const otherSum = others.reduce((s, r) => s + r.value, 0);
      main.push({ name: "Other", value: otherSum });
    }

    // Add remaining slice against budget (if budget provided)
    const remaining = Math.max(0, budget - used);
    const dataForPie = [...main];
    if (budget > 0 && remaining > 0) {
      dataForPie.push({ name: "Remaining", value: remaining });
    }

    return {
      chartData: dataForPie,
      totalUsed: used,
      remaining,
    };
  }, [expenses, budgetAmount]);

  const percentOfBudget = (value) => {
    const total = Number(budgetAmount || 0) || 1;
    return ((Number(value) / total) * 100).toFixed(1) + "%";
  };

  return (
    <div className="pie-wrap">
      <div className="pie-header">
        <div>
          <h3>Category Breakdown</h3>
          <p className="muted">
            {monthLabel} · Budget: <strong>{fmtCurrency(budgetAmount)}</strong>
          </p>
        </div>
        <div className="totals">
          <div>
            <span className="muted">Used</span>
            <strong>{fmtCurrency(totalUsed)}</strong>
          </div>
          <div>
            <span className="muted">Remaining</span>
            <strong>{fmtCurrency(remaining)}</strong>
          </div>
        </div>
      </div>

      <div className="pie-chart-card">
        <ResponsiveContainer width="100%" height={380}>
          <PieChart>
            <Pie
              data={chartData}
              dataKey="value"
              nameKey="name"
              innerRadius={70}
              outerRadius={120}
              paddingAngle={1.5}
            >
              {chartData.map((entry, idx) => (
                <Cell key={`cell-${idx}`} fill={COLORS[idx % COLORS.length]} />
              ))}
              <Label value={fmtCurrency(totalUsed, 0, 0)} position="center" />
            </Pie>

            <Tooltip
              formatter={(v, n) => [
                `${fmtCurrency(v)} (${percentOfBudget(v)})`,
                n,
              ]}
            />

            {/* Custom, wrapping legend */}
            <Legend
              verticalAlign="bottom"
              align="center"
              height={90}
              content={<CustomLegend />}
            />
          </PieChart>
        </ResponsiveContainer>
      </div>

      <div className="pie-note muted">
        Percentages are computed against the monthly budget. Small categories are
        grouped into “Other” to keep the legend readable.
      </div>
    </div>
  );
}
