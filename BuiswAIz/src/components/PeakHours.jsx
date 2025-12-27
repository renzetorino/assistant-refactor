import React, { useMemo, useState } from 'react';
import { LineChart, Line, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer } from 'recharts';

const PeakHours = ({ orderData }) => {
  const [selectedInterval, setSelectedInterval] = useState(null);
  const [showModal, setShowModal] = useState(false);
  const [showHelp, setShowHelp] = useState(false);

  // Process order data to get ORDERS (not items) by 3-hour intervals with detailed order info
  const peakHoursData = useMemo(() => {
    if (!orderData || orderData.length === 0) return [];

    // Define 3-hour intervals
    const intervals = [
      { label: '12AM-3AM', start: 0, end: 3 },
      { label: '3AM-6AM', start: 3, end: 6 },
      { label: '6AM-9AM', start: 6, end: 9 },
      { label: '9AM-12PM', start: 9, end: 12 },
      { label: '12PM-3PM', start: 12, end: 15 },
      { label: '3PM-6PM', start: 15, end: 18 },
      { label: '6PM-9PM', start: 18, end: 21 },
      { label: '9PM-12AM', start: 21, end: 24 }
    ];

    // Initialize counts for each interval
    const intervalCounts = intervals.map(interval => ({
      timeRange: interval.label,
      orders: 0,
      revenue: 0,
      orderIds: new Set(), // Track unique order IDs
      orderDetails: [] // Store detailed order information
    }));

    // Process each order item - use orders.orderdate (matches filtering logic)
    orderData.forEach(item => {
      const orderDate = item.orders?.orderdate || item.createdat;
      if (!orderDate) return;
      
      const date = new Date(orderDate);
      if (isNaN(date.getTime())) return;
      
      const hour = date.getHours();
      
      // Find which interval this hour belongs to
      const intervalIndex = intervals.findIndex(interval => 
        hour >= interval.start && hour < interval.end
      );
      
      if (intervalIndex !== -1) {
        // Add the order ID to the set (automatically handles duplicates)
        intervalCounts[intervalIndex].orderIds.add(item.orderid);
        intervalCounts[intervalIndex].revenue += (item.subtotal || 0);
        intervalCounts[intervalIndex].orderDetails.push({
          productName: item.products?.productname || 'Unknown Product',
          productImage: item.products?.image_url || '',
          quantity: item.quantity || 0,
          unitPrice: item.unitprice || 0,
          subtotal: item.subtotal || 0,
          orderDate: orderDate,
          orderId: item.orderid,
          color: item.productcategory?.color || '',
          ageSize: item.productcategory?.agesize || ''
        });
      }
    });

    // Convert Sets to counts and return
    return intervalCounts.map(interval => ({
      timeRange: interval.timeRange,
      orders: interval.orderIds.size, // Count of unique orders
      revenue: interval.revenue,
      orderDetails: interval.orderDetails
    }));
  }, [orderData]);

  // Custom tooltip for the chart
  const CustomTooltip = ({ active, payload, label }) => {
    if (active && payload && payload.length) {
      const data = payload[0].payload;
      return (
        <div className="peak-hours-tooltip">
          <p className="tooltip-label">{label}</p>
          <p className="tooltip-sales">
            Orders: <span className="tooltip-value">{data.orders}</span>
          </p>
          <p className="tooltip-revenue">
            Revenue: <span className="tooltip-value">₱{data.revenue.toLocaleString()}</span>
          </p>
          <p style={{ fontSize: '10px', color: '#999', marginTop: '4px' }}>
            Click the dot to see details
          </p>
        </div>
      );
    }
    return null;
  };

  // Find peak hour for highlight
  const peakInterval = useMemo(() => {
    return peakHoursData.reduce((max, current) => 
      current.orders > max.orders ? current : max, 
      { orders: 0, timeRange: '' }
    );
  }, [peakHoursData]);

  // Handle dot click
  const handleDotClick = (data) => {
    if (data && data.orders > 0) {
      setSelectedInterval(data);
      setShowModal(true);
    }
  };

  // Close modal
  const closeModal = () => {
    setShowModal(false);
    setSelectedInterval(null);
  };

  // Group orders by unique products for the modal (like original)
  const getGroupedProducts = (orderDetails) => {
    const grouped = {};
    orderDetails.forEach(detail => {
      // Create a unique key with product name, color, and size
      const colorText = detail.color ? ` (${detail.color}` : '';
      const sizeText = detail.ageSize ? `${colorText ? '' : ' ('}${detail.ageSize}` : '';
      const closeParenText = (colorText || sizeText) ? ')' : '';
      const fullProductName = `${detail.productName}${colorText}${sizeText}${closeParenText}`;
      
      if (!grouped[fullProductName]) {
        grouped[fullProductName] = {
          productName: detail.productName,
          fullProductName: fullProductName,
          productImage: detail.productImage,
          color: detail.color,
          ageSize: detail.ageSize,
          orders: []
        };
      }
      grouped[fullProductName].orders.push(detail);
    });
    return Object.values(grouped);
  };

  return (
    <div className="peak-hours-wrapper">
      <div className="peak-hours-header">
        <div className="panel-header-with-help">
          <div className="header-left-dash">
            <h3>Peak Hours Orders</h3>
            <div className="help-wrapper-dash">
              <button 
                className="help-button-dash"
                onClick={() => setShowHelp(!showHelp)}
                aria-label="Help"
              >
                ?
              </button>
              {showHelp && (
                <div className="help-box-dash">
                  <div className="help-arrow-dash"></div>
                  
                  <div className="help-content-dash">
                    <p>GEN TIPS</p>
                  </div>
                  
                  <div className="help-separator-dash"></div>
                  
                  <div className="help-content-dash">
                    <p> AI TIPS</p>
                  </div>
                </div>
              )}
            </div>
          </div>
        </div>
        {peakInterval.orders > 0 && (
          <div className="peak-indicator">
            <span className="peak-time">Peak: {peakInterval.timeRange}</span>
            <span className="peak-count">({peakInterval.orders} orders)</span>
          </div>
        )}
      </div>
      
      <div className="peak-hours-chart-container">
        {peakHoursData.length > 0 ? (
          <ResponsiveContainer width="100%" height="100%">
            <LineChart
              data={peakHoursData}
              margin={{
                top: 10,
                right: 10,
                left: 0,
                bottom: 20
              }}
            >
              <CartesianGrid strokeDasharray="3 3" stroke="#f0f0f0" />
              <XAxis 
                dataKey="timeRange" 
                tick={{ fontSize: 10, fill: '#666' }}
                angle={-20}
                textAnchor="end"
                height={10}
                interval={0}
              />
              <YAxis 
                tick={{ fontSize: 10, fill: '#666' }}
                label={{ value: 'Orders Count', angle: -90, position: 'insideLeft', style: { textAnchor: 'middle', fontSize: '10px', fill: '#666' } }}
              />
              <Tooltip content={<CustomTooltip />} />
              <Line 
                type="monotone" 
                dataKey="orders" 
                stroke="#04B4FC" 
                strokeWidth={2}
                dot={{ 
                  fill: '#04B4FC', 
                  strokeWidth: 2, 
                  r: 4,
                  cursor: 'pointer'
                }}
                activeDot={{ 
                  r: 6, 
                  stroke: '#04B4FC', 
                  strokeWidth: 2, 
                  fill: '#ffffff',
                  cursor: 'pointer',
                  onClick: (e, payload) => handleDotClick(payload.payload)
                }}
                onClick={(data) => handleDotClick(data)}
              />
            </LineChart>
          </ResponsiveContainer>
        ) : (
          <div className="no-data-message">
            <p>No sales data available</p>
            <small>Sales data will appear here once orders are recorded</small>
          </div>
        )}
      </div>

      {peakHoursData.length > 0 && (
        <div className="peak-hours-summary">
          <div className="summary-stats">
            <div className="stat-item">
              <span className="stat-label">Total Orders:</span>
              <span className="stat-value">
                {peakHoursData.reduce((sum, item) => sum + item.orders, 0)}
              </span>
            </div>
            <div className="stat-item">
              <span className="stat-label">Busiest Period:</span>
              <span className="stat-value">{peakInterval.timeRange}</span>
            </div>
          </div>
        </div>
      )}

      {/* Modal for detailed view */}
      {showModal && selectedInterval && (
        <div className="peak-hours-modal-overlay" onClick={closeModal}>
          <div className="peak-hours-modal" onClick={(e) => e.stopPropagation()}>
            <div className="peak-hours-modal-header">
              <h2>Orders Details: {selectedInterval.timeRange}</h2>
              <button className="peak-hours-modal-close" onClick={closeModal}>Close</button>
            </div>
            
            <div className="peak-hours-modal-summary">
              <div className="modal-summary-item">
                <span className="modal-summary-label">Total Orders:</span>
                <span className="modal-summary-value">{selectedInterval.orders}</span>
              </div>
              <div className="modal-summary-item">
                <span className="modal-summary-label">Total Revenue:</span>
                <span className="modal-summary-value">₱{selectedInterval.revenue.toLocaleString()}</span>
              </div>
            </div>

            <div className="peak-hours-modal-content">
              <h3>Products Sold</h3>
              <div className="peak-hours-products-list">
                {getGroupedProducts(selectedInterval.orderDetails).map((product, idx) => (
                  <div key={idx} className="product-section">
                    <div className="product-header">
                      <strong>Product Name:</strong> {product.fullProductName}
                    </div>
                    <table className="product-orders-table">
                      <thead>
                        <tr>
                          <th>Order ID</th>
                          <th>Date/Time</th>
                          <th>Quantity</th>
                          <th>Unit Price</th>
                          <th>Subtotal</th>
                        </tr>
                      </thead>
                      <tbody>
                        {product.orders.map((order, orderIdx) => (
                          <tr key={orderIdx}>
                            <td>{order.orderId}</td>
                            <td>
                              {new Date(order.orderDate).toLocaleString('en-US', {
                                year: 'numeric',
                                month: 'short',
                                day: 'numeric',
                                hour: '2-digit',
                                minute: '2-digit'
                              })}
                            </td>
                            <td>{order.quantity}</td>
                            <td>₱{order.unitPrice.toLocaleString()}</td>
                            <td className="subtotal-cell">₱{order.subtotal.toLocaleString()}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                ))}
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default PeakHours;