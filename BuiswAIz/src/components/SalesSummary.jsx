import React, { useMemo, useState, useEffect, useCallback } from 'react';
import { supabase } from '../supabase';
import '../stylecss/Sales/SalesSummary.css';

const SalesSummary = ({ orderData, rangeMode, selectedYear, selectedMonth, selectedDay }) => {
  const [expenses, setExpenses] = useState([]);
  const [_loading, setLoading] = useState(true);
  const [isTransactionsExpanded, setIsTransactionsExpanded] = useState(false);
  const [isSummaryExpanded, setIsSummaryExpanded] = useState(false);

  // Fetch expenses data
  useEffect(() => {
    const fetchExpenses = async () => {
      try {
        const { data, error } = await supabase
          .from('expenses')
          .select('amount, occurred_on');

        if (!error) setExpenses(data || []);
      } finally {
        setLoading(false);
      }
    };

    fetchExpenses();
  }, []);

  // Filter expenses based on calendar selection
  const filteredExpenses = useMemo(() => {
    return expenses.filter(expense => {
      const occurredOn = expense.occurred_on;
      if (!occurredOn) return false;

      const date = new Date(occurredOn);
      if (isNaN(date.getTime())) return false;

      switch (rangeMode) {
        case 'year':
          return date.getFullYear() === selectedYear;
        
        case 'month': {
          const [y, m] = selectedMonth.split('-').map(Number);
          return date.getFullYear() === y && date.getMonth() + 1 === m;
        }
        
        case 'day':
          return date.toISOString().slice(0, 10) === selectedDay;
        
        default:
          return true;
      }
    });
  }, [expenses, rangeMode, selectedYear, selectedMonth, selectedDay]);

  // Get filtered orders for transactions display
  const getFilteredOrders = useCallback(() => {
    const uniqueTransactions = new Map();
    orderData.forEach(item => {
      if (!uniqueTransactions.has(item.orderid)) {
        uniqueTransactions.set(item.orderid, {
          orderid: item.orderid,
          date: item.orders?.orderdate,
          amount: item.orders?.totalamount || 0,
          status: item.orders?.orderstatus || 'INCOMPLETE'
        });
      }
    });

    return Array.from(uniqueTransactions.values()).sort((a, b) => {
      const dateA = new Date(a.date);
      const dateB = new Date(b.date);
      return dateB - dateA;
    });
  }, [orderData]);

  // Calculate financial metrics
  const financialMetrics = useMemo(() => {
    if (!orderData.length) {
      return {
        netSales: 0,
        cogs: 0,
        grossProfit: 0,
        totalExpenses: 0,
        netProfit: 0,
        totalCustomers: 0,
        transactions: []
      };
    }

    const transactions = getFilteredOrders();

    const uniqueOrders = new Map();
    orderData.forEach(item => {
      if (!uniqueOrders.has(item.orderid)) {
        uniqueOrders.set(item.orderid, item.orders?.totalamount || 0);
      }
    });
    const netSales = Array.from(uniqueOrders.values()).reduce((sum, amount) => sum + amount, 0);
    const totalCustomers = uniqueOrders.size;

    const cogs = orderData.reduce((sum, item) => {
      const cost = item.productcategory?.cost || 0;
      const quantity = item.quantity || 0;
      return sum + (cost * quantity);
    }, 0);

    const grossProfit = netSales - cogs;
    const totalExpenses = filteredExpenses.reduce((sum, expense) => sum + (expense.amount || 0), 0);
    const netProfit = grossProfit - totalExpenses;

    return {
      netSales,
      cogs,
      grossProfit,
      totalExpenses,
      netProfit,
      totalCustomers,
      transactions
    };
  }, [orderData, filteredExpenses, getFilteredOrders]);

  const formatCurrency = (amount) => {
    return new Intl.NumberFormat('en-PH', {
      style: 'currency',
      currency: 'PHP',
      minimumFractionDigits: 2
    }).format(amount);
  };

  const formatDate = (dateString) => {
    const date = new Date(dateString);
    return date.toLocaleDateString('en-PH', {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit'
    });
  };

  const toggleTransactions = () => {
    setIsTransactionsExpanded(!isTransactionsExpanded);
  };

  const toggleSummary = () => {
    setIsSummaryExpanded(!isSummaryExpanded);
  };

  const getPeriodLabel = () => {
    switch (rangeMode) {
      case 'year':
        return `Year ${selectedYear}`;
      case 'month': {
        const [y, m] = selectedMonth.split('-');
        return `${new Date(y, m - 1).toLocaleString('default', { month: 'long' })} ${y}`;
      }
      case 'day':
        return new Date(selectedDay).toLocaleDateString('en-US', { 
          year: 'numeric', 
          month: 'long', 
          day: 'numeric' 
        });
      default:
        return 'All Time';
    }
  };

  return (
    <div className={`net-income-container ${isSummaryExpanded ? 'expanded' : ''}`}>
      <div className="net-income-header">
        <h3>Sales Summary - {getPeriodLabel()}</h3>
        <div className="header-controls">
          <button 
            className="summary-expand-btn" 
            onClick={toggleSummary}
            title={isSummaryExpanded ? "Collapse Summary" : "Expand Summary"}
          >
            {isSummaryExpanded ? '🗙' : '☰'}
          </button>
        </div>
      </div>

      <div className="metrics-grid">
        <div className="metric-card net-sales-card">
          <div className="metric-content">
            <p className="metric-label">Net Sales</p>
            <p className="metric-value">{formatCurrency(financialMetrics.netSales)}</p>
            <p className="metric-description">Total revenue from orders</p>
          </div>
        </div>

        <div className="metric-card cogs-card">
          <div className="metric-content">
            <p className="metric-label">Cost of Goods Sold</p>
            <p className="metric-value cogs-value">{formatCurrency(financialMetrics.cogs)}</p>
            <p className="metric-description">Total product costs</p>
          </div>
        </div>

        <div className="metric-card gross-profit-card">
          <div className="metric-content">
            <p className="metric-label">Gross Profit</p>
            <p className={`metric-value ${financialMetrics.grossProfit >= 0 ? 'positive' : 'negative'}`}>
              {formatCurrency(financialMetrics.grossProfit)}
            </p>
            <p className="metric-description">Sales minus COGS</p>
            <div className="metric-badge">
              {financialMetrics.netSales > 0 
                ? `${((financialMetrics.grossProfit / financialMetrics.netSales) * 100).toFixed(1)}% margin`
                : '0% margin'}
            </div>
          </div>
        </div>

        <div className="metric-card expenses-card">
          <div className="metric-content">
            <p className="metric-label">Total Expenses</p>
            <p className="metric-value expenses-value">{formatCurrency(financialMetrics.totalExpenses)}</p>
            <p className="metric-description">Operating expenses</p>
          </div>
        </div>

        <div className={`metric-card customers-card ${isTransactionsExpanded ? 'expanded' : ''}`}>
          <div className="metric-content">
            <div className="metric-header-with-icon">
              <img 
                src="https://www.pikpng.com/pngl/b/75-757195_customer-clipart-end-user-customer-blue-icon-png.png" 
                alt="Customers Icon" 
                className="metric-icon"
              />
              <p className="metric-label">Total Transactions</p>
              <span className="expand-icon" onClick={toggleTransactions}>{isTransactionsExpanded ? '🗙' : '☰'}</span>
            </div>
            <p className="metric-value customers-value">{financialMetrics.totalCustomers.toLocaleString()}</p>
            <p className="metric-description">Number of Total Customers</p>
          </div>
          
          {isTransactionsExpanded && (
            <div className="transactions-list">
              <div className="transactions-header">
                <span>Order ID</span>
                <span>Date & Time</span>
                <span>Amount</span>
                <span>Status</span>
              </div>
              <div className="transactions-scroll">
                {financialMetrics.transactions.length > 0 ? (
                  financialMetrics.transactions.slice(0, 50).map((transaction) => (
                    <div key={transaction.orderid} className="transaction-item">
                      <span className="transaction-id">#{transaction.orderid}</span>
                      <span className="transaction-date">{formatDate(transaction.date)}</span>
                      <span className="transaction-amount">{formatCurrency(transaction.amount)}</span>
                      <span className={`transaction-status ${transaction.status.toLowerCase()}`}>
                        {transaction.status}
                      </span>
                    </div>
                  ))
                ) : (
                  <div className="no-transactions">No transactions found</div>
                )}
                {financialMetrics.transactions.length > 50 && (
                  <div className="transactions-more">
                    Showing 50 of {financialMetrics.transactions.length} transactions
                  </div>
                )}
              </div>
            </div>
          )}
        </div>

        {isSummaryExpanded && (
          <div className="expanded-summary-container">
            <div className="metric-card net-profit-card featured">
              <div className="metric-content">
                <p className="metric-label">Net Profit</p>
                <p className={`metric-value large ${financialMetrics.netProfit >= 0 ? 'positive' : 'negative'}`}>
                  {formatCurrency(financialMetrics.netProfit)}
                </p>
                <p className="metric-description">Gross profit minus expenses</p>
                <div className="profit-breakdown">
                  <div className="breakdown-item">
                    <span className="breakdown-label">Gross:</span>
                    <span className="breakdown-value">{formatCurrency(financialMetrics.grossProfit)}</span>
                  </div>
                  <div className="breakdown-divider">−</div>
                  <div className="breakdown-item">
                    <span className="breakdown-label">Expenses:</span>
                    <span className="breakdown-value">{formatCurrency(financialMetrics.totalExpenses)}</span>
                  </div>
                </div>
              </div>
            </div>

            <div className="metric-card summary-card1">
              <div className="summary-header">
                <h4>Quick Summary</h4>
              </div>
              <div className="summary-content">
                <div className="summary-row">
                  <span className="summary-label">Profit Margin:</span>
                  <span className={`summary-value ${financialMetrics.netProfit >= 0 ? 'positive' : 'negative'}`}>
                    {financialMetrics.netSales > 0 
                      ? `${((financialMetrics.netProfit / financialMetrics.netSales) * 100).toFixed(2)}%`
                      : '0%'}
                  </span>
                </div>
                <div className="summary-row">
                  <span className="summary-label">Expense Ratio:</span>
                  <span className="summary-value">
                    {financialMetrics.netSales > 0 
                      ? `${((financialMetrics.totalExpenses / financialMetrics.netSales) * 100).toFixed(2)}%`
                      : '0%'}
                  </span>
                </div>
                <div className="summary-row">
                  <span className="summary-label">Cost of Goods Sold Ratio:</span>
                  <span className="summary-value">
                    {financialMetrics.netSales > 0 
                      ? `${((financialMetrics.cogs / financialMetrics.netSales) * 100).toFixed(2)}%`
                      : '0%'}
                  </span>
                </div>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );
};

export default SalesSummary;