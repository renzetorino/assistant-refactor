export default function SummaryCards({ daily, monthly, budget, onEditBudget }) {
  return (
    <div className="top-summary">
      <div className="summary-card"><h3>Daily Expenses</h3><p>₱{daily.toFixed(2)}</p></div>
      <div className="summary-card"><h3>Monthly Expenses</h3><p>₱{monthly.toFixed(2)}</p></div>
      <div className="summary-card">
        <h3>Remaining Budget</h3>
        <p style={{ color: monthly > budget ? "red" : "green" }}>
          {monthly > budget
            ? `Over by ₱${(monthly - budget).toFixed(2)}`
            : `₱${(budget - monthly).toFixed(2)} left`}
        </p>
      </div>
      <div className="summary-card">
        <h3>Monthly Budget</h3>
        <p>₱{budget.toFixed(2)}</p>
        <button onClick={onEditBudget}>Edit Budget</button>
      </div>
    </div>
  );
}
