import React, { useState, useEffect } from 'react';
import { supabase } from '../supabase';
import '../stylecss/Dashboard/DailyGrossSales.css';
import UploadSheets, { downloadTemplate } from '../components/UploadSheets'; 


const DailyGrossSales = () => {
  const [dailySales, setDailySales] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [maxSaleValue, setMaxSaleValue] = useState(0);
  const [showUploadModal, setShowUploadModal] = useState(false);
  const [showSalesModal, setShowSalesModal] = useState(false);
  const [selectedDaySales, setSelectedDaySales] = useState(null);
  const [salesDetails, setSalesDetails] = useState([]);
  const [loadingSalesDetails, setLoadingSalesDetails] = useState(false);

  useEffect(() => {
    fetchDailySales();
  }, []);

  const fetchDailySales = async () => {
    try {
      setLoading(true);

      const days = [];
      const today = new Date();
      for (let i = 9; i >= 0; i--) {
        const date = new Date(today);
        date.setDate(date.getDate() - i);
        days.push({
        date: date, 
          dateString: date.toLocaleDateString('en-CA', { timeZone: 'Asia/Manila' }), 
          dayName: date.toLocaleDateString('en-PH', { month: 'short', day: 'numeric', timeZone: 'Asia/Manila' })
        });
      }

      const salesPromises = days.map(async (day) => {
        const nextDay = new Date(day.date);
        nextDay.setDate(nextDay.getDate() + 1);
        const nextDayString = nextDay.toLocaleDateString('en-CA', { timeZone: 'Asia/Manila' });

        const { data, error } = await supabase
          .from('orders')
          .select('totalamount, orderdate')
          .gte('orderdate', day.dateString)
          .lt('orderdate', nextDayString);

        if (error) {
          return { ...day, totalSales: 0, transactionCount: 0 };
        }

        const totalSales = data.reduce((sum, order) => sum + (Number(String(order.totalamount).replace(/,/g, '')) || 0), 0);
        const transactionCount = data.length;

        return { ...day, totalSales, transactionCount };
      });

      const salesData = await Promise.all(salesPromises);
      const maxValue = Math.max(...salesData.map(day => day.totalSales));
      setMaxSaleValue(maxValue);

      const salesWithChanges = salesData.map((day, index) => {
        let changeAmount = 0;
        let changeDirection = 'same';

        if (index > 0) {
          const previousDay = salesData[index - 1];
          changeAmount = day.totalSales - previousDay.totalSales;

          if (changeAmount > 0) changeDirection = 'up';
          else if (changeAmount < 0) changeDirection = 'down';
        }

        return { ...day, changeAmount, changeDirection };
      });

      setDailySales(salesWithChanges);
      setError(null);
    } catch {
      setError('Failed to load daily sales data');
    } finally {
      setLoading(false);
    }
  };

  const fetchSalesDetails = async (dateString) => {
    try {
      setLoadingSalesDetails(true);
      
      const nextDay = new Date(dateString);
      nextDay.setDate(nextDay.getDate() + 1);
      const nextDayString = nextDay.toLocaleDateString('en-CA', { timeZone: 'Asia/Manila' });

      const { data: orders, error: ordersError } = await supabase
        .from('orders')
        .select('orderid, totalamount, orderdate')
        .gte('orderdate', dateString)
        .lt('orderdate', nextDayString);

      if (ordersError) throw ordersError;

      if (orders.length === 0) {
        setSalesDetails([]);
        return;
      }

      const orderIds = orders.map(o => o.orderid);

      const { data: orderItems, error: itemsError } = await supabase
        .from('orderitems')
        .select(`
          orderid,
          productid,
          productcategoryid,
          quantity,
          unitprice,
          subtotal,
          productcategory (
            color,
            agesize,
            products (
              productname,
              image_url
            )
          )
        `)
        .in('orderid', orderIds);

      if (itemsError) throw itemsError;

      const itemsMap = {};
      orderItems.forEach(item => {
        const id = item.productcategoryid || item.productid;
        const name = item.productcategory?.products?.productname || 'Unknown Product';
        const imageUrl = item.productcategory?.products?.image_url || '';
        const color = item.productcategory?.color || '';
        const agesize = item.productcategory?.agesize || '';
        
        // Build category/variant display
        const variantParts = [color, agesize].filter(v => v);
        const category = variantParts.length > 0 ? variantParts.join(', ') : 'No Variant';
        
        const price = Number(String(item.unitprice).replace(/,/g, '')) || 0;

        if (!itemsMap[id]) {
          itemsMap[id] = {
            productid: item.productid,
            productcategoryid: id,
            productname: name,
            image_url: imageUrl,
            category: category,
            quantity: 0,
            unitprice: price,
            totalAmount: 0
          };
        }

        itemsMap[id].quantity += item.quantity;
        itemsMap[id].totalAmount += Number(String(item.subtotal).replace(/,/g, '')) || 0;
      });

      const detailsArray = Object.values(itemsMap);
      detailsArray.sort((a, b) => b.totalAmount - a.totalAmount);

      setSalesDetails(detailsArray);
    } catch (err) {
      console.error('Error fetching sales details:', err);
      setSalesDetails([]);
    } finally {
      setLoadingSalesDetails(false);
    }
  };

  const handleBarClick = async (day) => {
    if (day.totalSales === 0) return;
    
    setSelectedDaySales(day);
    setShowSalesModal(true);
    await fetchSalesDetails(day.dateString);
  };

  const getBarHeight = (saleAmount) => {
    if (maxSaleValue === 0) return 0;
    return Math.max((saleAmount / maxSaleValue) * 100, 2);
  };

  const formatCurrency = (amount) =>
    `₱${amount.toLocaleString('en-PH', { minimumFractionDigits: 0, maximumFractionDigits: 0 })}`;

  const formatChange = (amount) => {
    const sign = amount >= 0 ? '+' : '-';
    return `${sign}₱${Math.abs(amount).toLocaleString('en-PH', {
      minimumFractionDigits: 0,
      maximumFractionDigits: 0
    })}`;
  };

  if (loading) {
    return (
      <div className="daily-sales-container">
        <div className="daily-sales-loading"><p>Loading daily sales data...</p></div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="daily-sales-container">
        <div className="daily-sales-error">
          <p>{error}</p>
          <button onClick={fetchDailySales} className="retry-button">Retry</button>
        </div>
      </div>
    );
  }

  return (
    <>
      <div className="daily-sales-container">
        <div className="daily-sales-header">
          <div className="daily-sales-header-left">
            <h4>Last 10 Days Performance</h4>
            <div className="sales-legend">
              <span className="legend-item">
                <div className="legend-color primary"></div>
                Daily Sales
              </span>
            </div>
          </div>
          <button 
            className="upload-sheet-button" 
            onClick={() => setShowUploadModal(true)}
          >
             Upload Sheet
          </button>
        </div>

        <div className="daily-sales-chart">
          <div className="chart-y-axis">
            {maxSaleValue > 0 && (
              <>
                <span className="y-axis-label">{formatCurrency(maxSaleValue)}</span>
                <span className="y-axis-label">{formatCurrency(maxSaleValue * 0.75)}</span>
                <span className="y-axis-label">{formatCurrency(maxSaleValue * 0.5)}</span>
                <span className="y-axis-label">{formatCurrency(maxSaleValue * 0.25)}</span>
                <span className="y-axis-label">₱0</span>
              </>
            )}
          </div>

          <div className="chart-bars-container">
            {dailySales.map((day, index) => (
              <div key={index} className="bar-group">
                <div className="bar-container">
                  <div
                    className="bar primary-bar"
                    style={{ 
                      height: `${getBarHeight(day.totalSales)}%`,
                      cursor: day.totalSales > 0 ? 'pointer' : 'default'
                    }}
                    title={`${day.dayName}: ${formatCurrency(day.totalSales)} - Click to view details`}
                    onClick={() => handleBarClick(day)}
                  >
                    <div className="bar-value">
                      {day.totalSales > 0 ? formatCurrency(day.totalSales) : ''}
                    </div>
                  </div>
                </div>
                <div className="bar-label">
                  <div className="day-name">{day.dayName}</div>
                  {index > 0 && day.changeAmount !== 0 && (
                    <div className={`change-indicator ${day.changeDirection}`}>
                      <span className={`change-icon ${day.changeDirection}`}>
                        {day.changeDirection === 'up' ? '↗️' : day.changeDirection === 'down' ? '↘️' : '➖'}
                      </span>
                      <span className="change-text">
                        {formatChange(day.changeAmount)}
                      </span>
                    </div>
                  )}
                  {index === 0 && (
                    <div className="change-indicator baseline">
                      <span className="change-text">Baseline</span>
                    </div>
                  )}
                </div>
              </div>
            ))}
          </div>
        </div>
      </div>

      {/* Upload Modal */}
      {showUploadModal && (
        <div className="modal-overlay" onClick={() => setShowUploadModal(false)}>
          <div className="modal-sheet" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <h2>Upload Sales Spreadsheet</h2>
              <button
                className="close-btn"
                onClick={() => setShowUploadModal(false)}
                aria-label="Close"
              >
                ✕
              </button>
            </div>
            {/* Toolbar above the uploader */}
            <div className="template-toolbar">
              <button
                type="button"
                className="download-template-btn"
                onClick={downloadTemplate}
                aria-label="Download sales upload template"
              >
                {/* Icon */}
                <svg
                  width="18"
                  height="18"
                  viewBox="0 0 24 24"
                  fill="none"
                  aria-hidden="true"
                  className="icon"
                >
                  <path d="M12 3v10m0 0l4-4m-4 4l-4-4M4 17v2a2 2 0 002 2h12a2 2 0 002-2v-2"
                        stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round"/>
                </svg>
                <span>Download Template</span>
              </button>

              <div className="template-help">
                Expected columns:&nbsp;
                <code>orderid</code>, <code>orderdate</code>, <code>productname</code>,
                <code> color</code>, <code>agesize</code>, <code>quantity</code>,
                <code> unitprice</code>, <code>subtotal</code>, <code>amountpaid</code>
              </div>
            </div>

            <UploadSheets />

            <div className="modal-actions">
              <button onClick={() => setShowUploadModal(false)}>Close</button>
            </div>
          </div>
        </div>
      )}


      {/* Sales Details Modal */}
      {showSalesModal && selectedDaySales && (
        <div className="modal-overlay" onClick={() => setShowSalesModal(false)}>
          <div className="modal sales-details-modal" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <h2>Sales Details - {selectedDaySales.dayName}</h2>
              <button
                className="close-btn"
                onClick={() => setShowSalesModal(false)}
                aria-label="Close"
              >
                ✕
              </button>
            </div>

            <div className="sales-summary-info">
              <div className="summary-item">
                <span className="summary-label">Total Sales:</span>
                <span className="summary-value">{formatCurrency(selectedDaySales.totalSales)}</span>
              </div>
              <div className="summary-item">
                <span className="summary-label">Transactions:</span>
                <span className="summary-value">{selectedDaySales.transactionCount}</span>
              </div>
            </div>

            <div className="sales-details-content">
              {loadingSalesDetails ? (
                <div className="loading-state">
                  <p>Loading sales details...</p>
                </div>
              ) : salesDetails.length === 0 ? (
                <div className="empty-state">
                  <p>No items found for this date.</p>
                </div>
              ) : (
                <div className="sales-items-list">
                  <table className="sales-items-table">
                    <thead>
                      <tr>
                        <th>Product</th>
                        <th>Category</th>
                        <th>Quantity</th>
                        <th>Unit Price</th>
                        <th>Total</th>
                      </tr>
                    </thead>
                    <tbody>
                      {salesDetails.map((item) => (
                        <tr key={item.productid}>
                          <td>
                            <span className="product-name">{item.productname}</span>
                          </td>
                          <td>
                            <span className="product-category">{item.category}</span>
                          </td>
                          <td className="text-center">{item.quantity}</td>
                          <td className="text-right">{formatCurrency(item.unitprice)}</td>
                          <td className="text-right total-amount">{formatCurrency(item.totalAmount)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </div>

            <div className="modal-actions">
              <button onClick={() => setShowSalesModal(false)}>Close</button>
            </div>
          </div>
        </div>
      )}
    </>
  );
};

export default DailyGrossSales;