import React, { useMemo, useState, useEffect, useCallback } from 'react';
import { supabase } from '../supabase';
import '../stylecss/Sales/SalesSummary.css';

const SalesSummary = ({ orderData, statsFilter }) => {
  const [expenses, setExpenses] = useState([]);
  const [_loading, setLoading] = useState(true);
  const [localFilter, setLocalFilter] = useState(statsFilter || 'all');
  const [_previousEarnings, setPreviousEarnings] = useState(0);
  const [percentageChange, setPercentageChange] = useState(0);
  const [isIncreasing, setIsIncreasing] = useState(null);
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

  // Sync localFilter with statsFilter prop
  useEffect(() => {
    setLocalFilter(statsFilter || 'all');
  }, [statsFilter]);

  // Get available years from orderData
  const getAvailableYears = () => {
    const years = new Set();
    const currentYear = new Date().getFullYear();
    orderData.forEach(item => {
      const orderDate = item.orders?.orderdate;
      if (!orderDate) return;
      const date = new Date(orderDate);
      if (!isNaN(date.getTime())) {
        years.add(date.getFullYear());
      }
    });
    return Array.from(years).sort((a, b) => b - a).map(year => ({
      value: `year-${year}`,
      label: year === currentYear ? 'This Year' : year.toString()
    }));
  };

  const availableYears = getAvailableYears();

  // Get filtered orders for transactions display - converted to useCallback
  const getFilteredOrders = useCallback(() => {
    const now = new Date();
    const currentFilter = localFilter;

    const filteredOrderData = orderData.filter(item => {
      const orderDate = item.orders?.orderdate;
      if (!orderDate) return false;

      const date = new Date(orderDate);

      if (currentFilter.startsWith('year-')) {
        const year = parseInt(currentFilter.replace('year-', ''));
        return date.getFullYear() === year;
      }

      switch (currentFilter) {
        case 'all':
          return true;
        case 'today':
          return date.toDateString() === now.toDateString();
        case 'week1':
          return date.getDate() <= 7 && date.getMonth() === now.getMonth() && date.getFullYear() === now.getFullYear();
        case 'week2':
          return date.getDate() > 7 && date.getDate() <= 14 && date.getMonth() === now.getMonth() && date.getFullYear() === now.getFullYear();
        case 'week3':
          return date.getDate() > 14 && date.getDate() <= 21 && date.getMonth() === now.getMonth() && date.getFullYear() === now.getFullYear();
        case 'month':
          return date.getMonth() === now.getMonth() && date.getFullYear() === now.getFullYear();
        default:
          return true;
      }
    });

    // Group by orderid to get unique transactions with dates
    const uniqueTransactions = new Map();
    filteredOrderData.forEach(item => {
      if (!uniqueTransactions.has(item.orderid)) {
        uniqueTransactions.set(item.orderid, {
          orderid: item.orderid,
          date: item.orders?.orderdate,
          amount: item.orders?.totalamount || 0,
          status: item.orders?.orderstatus || 'INCOMPLETE'
        });
      }
    });

    // Convert to array and sort by date (newest first)
    return Array.from(uniqueTransactions.values()).sort((a, b) => {
      const dateA = new Date(a.date);
      const dateB = new Date(b.date);
      return dateB - dateA;
    });
  }, [orderData, localFilter]);

  // Calculate financial metrics including total customers
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
    const now = new Date();
    const currentFilter = localFilter;

    const filteredOrderData = orderData.filter(item => {
      const orderDate = item.orders?.orderdate;
      if (!orderDate) return false;

      const date = new Date(orderDate);

      if (currentFilter.startsWith('year-')) {
        const year = parseInt(currentFilter.replace('year-', ''));
        return date.getFullYear() === year;
      }

      switch (currentFilter) {
        case 'all':
          return true;
        case 'today':
          return date.toDateString() === now.toDateString();
        case 'week1':
          return date.getDate() <= 7 && date.getMonth() === now.getMonth() && date.getFullYear() === now.getFullYear();
        case 'week2':
          return date.getDate() > 7 && date.getDate() <= 14 && date.getMonth() === now.getMonth() && date.getFullYear() === now.getFullYear();
        case 'week3':
          return date.getDate() > 14 && date.getDate() <= 21 && date.getMonth() === now.getMonth() && date.getFullYear() === now.getFullYear();
        case 'month':
          return date.getMonth() === now.getMonth() && date.getFullYear() === now.getFullYear();
        default:
          return true;
      }
    });

    const filteredExpenses = expenses.filter(expense => {
      const occurredOn = expense.occurred_on;
      if (!occurredOn) return false;

      const date = new Date(occurredOn);

      if (currentFilter.startsWith('year-')) {
        const year = parseInt(currentFilter.replace('year-', ''));
        return date.getFullYear() === year;
      }

      switch (currentFilter) {
        case 'all':
          return true;
        case 'today':
          return date.toDateString() === now.toDateString();
        case 'week1':
          return date.getDate() <= 7 && date.getMonth() === now.getMonth() && date.getFullYear() === now.getFullYear();
        case 'week2':
          return date.getDate() > 7 && date.getDate() <= 14 && date.getMonth() === now.getMonth() && date.getFullYear() === now.getFullYear();
        case 'week3':
          return date.getDate() > 14 && date.getDate() <= 21 && date.getMonth() === now.getMonth() && date.getFullYear() === now.getFullYear();
        case 'month':
          return date.getMonth() === now.getMonth() && date.getFullYear() === now.getFullYear();
        default:
          return true;
      }
    });

    const uniqueOrders = new Map();
    filteredOrderData.forEach(item => {
      if (!uniqueOrders.has(item.orderid)) {
        uniqueOrders.set(item.orderid, item.orders?.totalamount || 0);
      }
    });
    const netSales = Array.from(uniqueOrders.values()).reduce((sum, amount) => sum + amount, 0);
    const totalCustomers = uniqueOrders.size;

    const cogs = filteredOrderData.reduce((sum, item) => {
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
  }, [orderData, expenses, localFilter, getFilteredOrders]);

  // Calculate sales trend (percentage change from previous period)
  useEffect(() => {
    if (localFilter === 'all') {
      setPercentageChange(0);
      setIsIncreasing(null);
    } else if (orderData && orderData.length > 0) {
      calculatePreviousPeriodEarnings();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [orderData, localFilter, financialMetrics.netSales]);

  const calculatePreviousPeriodEarnings = () => {
    const now = new Date();
    let previousPeriodData = [];

    if (localFilter.startsWith('year-')) {
      const selectedYear = parseInt(localFilter.replace('year-', ''));
      const previousYear = selectedYear - 1;
      
      previousPeriodData = orderData.filter(item => {
        const orderDate = item.orders?.orderdate;
        if (!orderDate) return false;
        const date = new Date(orderDate);
        return date.getFullYear() === previousYear;
      });
    } else {
      switch (localFilter) {
        case 'today': {
          const yesterday = new Date(now);
          yesterday.setDate(yesterday.getDate() - 1);
          previousPeriodData = orderData.filter(item => {
            const orderDate = item.orders?.orderdate;
            if (!orderDate) return false;
            const date = new Date(orderDate);
            return date.toDateString() === yesterday.toDateString();
          });
          break;
        }

        case 'week1': {
          const lastMonth = now.getMonth() === 0 ? 11 : now.getMonth() - 1;
          const lastYear = now.getMonth() === 0 ? now.getFullYear() - 1 : now.getFullYear();
          
          previousPeriodData = orderData.filter(item => {
            const orderDate = item.orders?.orderdate;
            if (!orderDate) return false;
            const date = new Date(orderDate);
            return date.getDate() <= 7 && 
                   date.getMonth() === lastMonth && 
                   date.getFullYear() === lastYear;
          });
          break;
        }

        case 'week2': {
          previousPeriodData = orderData.filter(item => {
            const orderDate = item.orders?.orderdate;
            if (!orderDate) return false;
            const date = new Date(orderDate);
            return date.getDate() <= 7 && 
                   date.getMonth() === now.getMonth() && 
                   date.getFullYear() === now.getFullYear();
          });
          break;
        }

        case 'week3': {
          previousPeriodData = orderData.filter(item => {
            const orderDate = item.orders?.orderdate;
            if (!orderDate) return false;
            const date = new Date(orderDate);
            return date.getDate() > 7 && date.getDate() <= 14 && 
                   date.getMonth() === now.getMonth() && 
                   date.getFullYear() === now.getFullYear();
          });
          break;
        }

        case 'month': {
          const lastMonth = now.getMonth() === 0 ? 11 : now.getMonth() - 1;
          const lastYear = now.getMonth() === 0 ? now.getFullYear() - 1 : now.getFullYear();
          
          previousPeriodData = orderData.filter(item => {
            const orderDate = item.orders?.orderdate;
            if (!orderDate) return false;
            const date = new Date(orderDate);
            return date.getMonth() === lastMonth && 
                   date.getFullYear() === lastYear;
          });
          break;
        }

        default:
          break;
      }
    }

    const uniqueOrders = new Map();
    previousPeriodData.forEach(item => {
      if (!uniqueOrders.has(item.orderid)) {
        uniqueOrders.set(item.orderid, item.orders?.totalamount || 0);
      }
    });
    const previousTotal = Array.from(uniqueOrders.values()).reduce((sum, amount) => sum + amount, 0);
    setPreviousEarnings(previousTotal);

    if (previousTotal === 0) {
      if (financialMetrics.netSales > 0) {
        setPercentageChange(100);
        setIsIncreasing(true);
      } else {
        setPercentageChange(0);
        setIsIncreasing(null);
      }
    } else {
      const change = ((financialMetrics.netSales - previousTotal) / previousTotal) * 100;
      setPercentageChange(Math.abs(change));
      setIsIncreasing(financialMetrics.netSales > previousTotal);
    }
  };

  const getPeriodLabel = () => {
    if (localFilter.startsWith('year-')) {
      const selectedYear = parseInt(localFilter.replace('year-', ''));
      return `vs ${selectedYear - 1}`;
    }

    switch (localFilter) {
      case 'today': return 'vs Yesterday';
      case 'week1': return 'vs Last Month (7 Days)';
      case 'week2': return 'vs Last 7 Days';
      case 'week3': return 'vs Last 14 Days';
      case 'month': return 'vs Last 21 Days';
      case 'all': return 'All Time Data';
      default: return 'vs Previous Period';
    }
  };

  const getTrendIcon = () => {
    if (isIncreasing === null) return null;
    
    return isIncreasing 
      ? "https://cdn-icons-png.freepik.com/256/5412/5412850.png"
      : "https://cdn-icons-png.freepik.com/512/8438/8438640.png";
  };

  const getTrendColor = () => {
    if (isIncreasing === null) return '#666';
    return isIncreasing ? '#28a745' : '#dc3545';
  };

  const getTrendText = () => {
    if (isIncreasing === null) return 'No Data';
    return isIncreasing ? 'Increase' : 'Decrease';
  };

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

  const handleFilterChange = (e) => {
    setLocalFilter(e.target.value);
  };

  const toggleTransactions = () => {
    setIsTransactionsExpanded(!isTransactionsExpanded);
  };

  const toggleSummary = () => {
    setIsSummaryExpanded(!isSummaryExpanded);
  };

  return (
    <div className={`net-income-container ${isSummaryExpanded ? 'expanded' : ''}`}>
      <div className="net-income-header">
        <h3>Sales Summary</h3>
        <div className="header-controls">
          <button 
            className="summary-expand-btn" 
            onClick={toggleSummary}
            title={isSummaryExpanded ? "Collapse Summary" : "Expand Summary"}
          >
            {isSummaryExpanded ? '🗙' : '☰'}
          </button>
          <select 
            className="filter-dropdown" 
            value={localFilter} 
            onChange={handleFilterChange}
          >
            <option value="all">All Time</option>
            <option value="today">Today</option>
            <option value="week1">1st Week</option>
            <option value="week2">2nd Week</option>
            <option value="week3">3rd Week</option>
            <option value="month">This Month</option>
            {availableYears.map(yearObj => (
              <option key={yearObj.value} value={yearObj.value}>{yearObj.label}</option>
            ))}
          </select>
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

        <div className="metric-card sales-trend-card">
          <div className="metric-content">
            <div className="metric-header-with-icon">
              {getTrendIcon() && localFilter !== 'all' && (
                <img 
                  src={getTrendIcon()} 
                  alt={`Sales ${getTrendText()}`}
                  className="metric-icon trend-icon"
                />
              )}
              <p className="metric-label">Sales Trend</p>
            </div>
            {localFilter === 'all' ? (
              <div className="trend-content">
                <p className="metric-value">Total Historical Data</p>
                <p className="metric-description">No comparison available</p>
              </div>
            ) : (
              <div className="trend-content">
                <p className="metric-value" style={{ color: getTrendColor(), fontSize: '1.5rem' }}>
                  {percentageChange.toFixed(1)}%
                </p>
                <span className="trend-status" style={{ color: getTrendColor(), fontWeight: '600' }}>
                  {getTrendText()}
                </span>
                <p className="metric-description" style={{ marginTop: '5px' }}>
                  {getPeriodLabel()}
                </p>
              </div>
            )}
          </div>
        </div>

        {isSummaryExpanded && (
          <>
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
          </>
        )}
      </div>
    </div>
  );
};

export default SalesSummary;