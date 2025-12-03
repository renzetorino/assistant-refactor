import React, { useState, useEffect } from 'react';
import { supabase } from '../supabase';
import '../stylecss/Dashboard/SalesSummaryDashboard.css';

const SalesSummaryDashboard = () => {
  const [salesData, setSalesData] = useState({
    todaysSale: 0,
    yesterdaysSale: 0,
    percentageChange: 0,
    dailyTransactions: 0,
    todaysExpenses: 0,
    todaysTotalUnitsSold: 0,
    totalDefectiveItems: 0
  });
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  useEffect(() => {
    fetchSalesData();
  }, []);

  const fetchSalesData = async () => {
    try {
      setLoading(true);

      const now = new Date();
      const phTimeFormatter = new Intl.DateTimeFormat('en-PH', {
        timeZone: 'Asia/Manila',
        year: 'numeric',
        month: '2-digit',
        day: '2-digit',
        hour12: false
      });
      const phTimeParts = phTimeFormatter.formatToParts(now);
      const currentYear = parseInt(phTimeParts.find(p => p.type === 'year').value);
      const currentMonth = parseInt(phTimeParts.find(p => p.type === 'month').value);
      const currentDay = parseInt(phTimeParts.find(p => p.type === 'day').value);

      const todayString = `${currentYear}-${String(currentMonth).padStart(2, '0')}-${String(currentDay).padStart(2, '0')}`;
      const todayDate = new Date(currentYear, currentMonth - 1, currentDay);
      const yesterdayDate = new Date(todayDate);
      yesterdayDate.setDate(yesterdayDate.getDate() - 1);
      const yesterdayString = `${yesterdayDate.getFullYear()}-${String(yesterdayDate.getMonth() + 1).padStart(2, '0')}-${String(yesterdayDate.getDate()).padStart(2, '0')}`;

      const todayStart = `${todayString} 00:00:00`;
      const todayEnd = `${todayString} 23:59:59`;

      let todaysSale = 0;
      let yesterdaysSale = 0;
      let dailyTransactions = 0;
      let todaysExpenses = 0;
      let todaysTotalUnitsSold = 0;
      let totalDefectiveItems = 0;

      // Fetch today's orders (Today's Sale)
      const { data: todaysOrders, error: todaysError } = await supabase
        .from('orders')
        .select('orderid, totalamount, orderdate')
        .gte('orderdate', todayStart)
        .lte('orderdate', todayEnd);

      if (todaysError) {
        console.warn('Failed to fetch today\'s orders:', todaysError);
      } else if (todaysOrders && todaysOrders.length > 0) {
        todaysSale = todaysOrders.reduce((sum, order) => {
          const amount = parseFloat(order.totalamount);
          return sum + (isNaN(amount) ? 0 : amount);
        }, 0);
        
        dailyTransactions = todaysOrders.length;

        // Fetch today's total units sold
        const { data: todaysOrderItems, error: orderItemsError } = await supabase
          .from('orderitems')
          .select('quantity, createdat')
          .in('orderid', todaysOrders.map(o => o.orderid));

        if (!orderItemsError && todaysOrderItems) {
          todaysTotalUnitsSold = todaysOrderItems.reduce((sum, item) => {
            return sum + (parseFloat(item.quantity) || 0);
          }, 0);
        }
      }

      // Fetch yesterday's sales for percentage change
      const { data: yesterdaysOrders } = await supabase
        .from('orders')
        .select('totalamount, orderdate')
        .gte('orderdate', `${yesterdayString} 00:00:00`)
        .lte('orderdate', `${yesterdayString} 23:59:59`);
      if (yesterdaysOrders) {
        yesterdaysSale = yesterdaysOrders.reduce((sum, order) => sum + (parseFloat(order.totalamount) || 0), 0);
      }

      // Fetch today's expenses
      const { data: todaysExpensesData, error: expensesError } = await supabase
        .from('expenses')
        .select('amount, occurred_on')
        .eq('occurred_on', todayString);

      if (!expensesError && todaysExpensesData) {
        todaysExpenses = todaysExpensesData.reduce((sum, expense) => {
          return sum + (parseFloat(expense.amount) || 0);
        }, 0);
      }

      // Fetch total defective items (all time)
      const { data: defectiveItemsData, error: defectiveError } = await supabase
        .from('defectiveitems')
        .select('quantity');

      if (!defectiveError && defectiveItemsData) {
        totalDefectiveItems = defectiveItemsData.reduce((sum, item) => {
          return sum + (parseFloat(item.quantity) || 0);
        }, 0);
      }

      // Calculate percentage change
      let percentageChange = 0;
      if (yesterdaysSale > 0) {
        percentageChange = ((todaysSale - yesterdaysSale) / yesterdaysSale) * 100;
      } else if (todaysSale > 0) {
        percentageChange = 100;
      }

      setSalesData({
        todaysSale,
        yesterdaysSale,
        percentageChange,
        dailyTransactions,
        todaysExpenses,
        todaysTotalUnitsSold,
        totalDefectiveItems
      });
      setError(null);
    } catch (error) {
      console.error('Error fetching sales data:', error);
      setError('Failed to load sales data: ' + error.message);
    } finally {
      setLoading(false);
    }
  };

  const downloadCSV = () => {
    const now = new Date();
    const dateStr = now.toLocaleDateString('en-PH');
    const timeStr = now.toLocaleTimeString('en-PH');

    const csvContent = [
      ['Sales Summary Report'],
      [`Generated on: ${dateStr} ${timeStr}`],
      [''],
      ['Metric', 'Value'],
      ['Today\'s Sale', `P${salesData.todaysSale.toFixed(2)}`],
      ['Percentage Change', `${formatPercentageChange(salesData.percentageChange)}`],
      ['Today\'s Transactions', salesData.dailyTransactions],
      ['Today\'s Expenses', `P${salesData.todaysExpenses.toFixed(2)}`],
      ['Today\'s Total Units Sold', salesData.todaysTotalUnitsSold],
      ['Total Defective Items', salesData.totalDefectiveItems]
    ].map(row => row.join(',')).join('\n');

    const blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8;' });
    const link = document.createElement('a');
    const url = URL.createObjectURL(blob);
    
    link.setAttribute('href', url);
    link.setAttribute('download', `Sales_Summary_${dateStr.replace(/\//g, '-')}.csv`);
    link.style.visibility = 'hidden';
    
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
  };

  const formatPercentageChange = (change) => {
    const absChange = Math.abs(change);
    const sign = change >= 0 ? '+' : '-';
    return `${sign}${absChange.toFixed(1)}%`;
  };

  const getChangeIcon = (change) => {
    if (change > 0) return '↗️';
    if (change < 0) return '↘️';
    return '➖';
  };

  const getChangeColor = (change) => {
    if (change > 0) return '#059669';
    if (change < 0) return '#dc2626';
    return '#6b7280';
  };

  if (loading) {
    return (
      <div className="sales-summary-container">
        <div className="loading-state">
          <p>Loading sales data...</p>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="sales-summary-container">
        <div className="error-state">
          <p>{error}</p>
          <button onClick={fetchSalesData} style={{ marginTop: '10px', padding: '5px 10px' }}>
            Retry
          </button>
        </div>
      </div>
    );
  }

  return (
    <div className="sales-summary-container">
      <div className="sales-summary-header">
        <h3>Sales Summary</h3>
        <button className="download-csv-btn" onClick={downloadCSV} title="Download as CSV">
          Download Summary
        </button>
      </div>

      <div className="sales-summary-grid">
        <div className="sales-card">
          <div className="sales-icon trend-up"><span>📈</span></div>
          <div className="sales-info">
            <div className="sales-amount">
              ₱{salesData.todaysSale.toLocaleString('en-PH', { minimumFractionDigits: 2 })}
            </div>
            <div className="sales-label">
              Today's Sale
              <span
                className="percentage-change"
                style={{
                  color: getChangeColor(salesData.percentageChange),
                  marginLeft: '4px',
                  fontSize: '9px',
                  fontWeight: '600'
                }}
              >
                {getChangeIcon(salesData.percentageChange)} {formatPercentageChange(salesData.percentageChange)}
              </span>
            </div>
          </div>
        </div>

        <div className="sales-card">
          <div className="sales-icon transactions"><span>🧾</span></div>
          <div className="sales-info">
            <div className="sales-amount">{salesData.dailyTransactions}</div>
            <div className="sales-label">Today's Transactions</div>
          </div>
        </div>

        <div className="sales-card">
          <div className="sales-icon dollar"><span>💸</span></div>
          <div className="sales-info">
            <div className="sales-amount">
              ₱{salesData.todaysExpenses.toLocaleString('en-PH', { minimumFractionDigits: 2 })}
            </div>
            <div className="sales-label">Today's Expenses</div>
          </div>
        </div>

        <div className="sales-card">
          <div className="sales-icon calendar"><span>📦</span></div>
          <div className="sales-info">
            <div className="sales-amount">{salesData.todaysTotalUnitsSold}</div>
            <div className="sales-label">Today's Total Units Sold</div>
          </div>
        </div>

        <div className="sales-card">
          <div className="sales-icon trend-up"><span>⚠️</span></div>
          <div className="sales-info">
            <div className="sales-amount" style={{ color: salesData.totalDefectiveItems > 0 ? '#dc2626' : '#059669' }}>
              {salesData.totalDefectiveItems}
            </div>
            <div className="sales-label">Total Defective Items</div>
          </div>
        </div>
      </div>
    </div>
  );
};

export default SalesSummaryDashboard;