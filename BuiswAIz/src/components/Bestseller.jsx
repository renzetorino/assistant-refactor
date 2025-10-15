import React, { useState, useMemo } from 'react';

const Bestseller = ({ bestsellers, orderData }) => {
  const [timeFilter, setTimeFilter] = useState('all');

  // Filter bestsellers based on time period
  const filteredBestsellers = useMemo(() => {
    if (!orderData || orderData.length === 0) return bestsellers;
    
    const now = new Date();
    
    // Filter orderData based on selected time period
    const filteredOrders = orderData.filter(item => {
      const date = new Date(item.orders?.orderdate || item.createdat);
      
      if (isNaN(date.getTime())) return false;

      switch (timeFilter) {
        case 'all':
          return true;
        
        case 'today':
          return date.toDateString() === now.toDateString();
        
        case 'weekly': {
          const oneWeekAgo = new Date(now);
          oneWeekAgo.setDate(now.getDate() - 7);
          return date >= oneWeekAgo;
        }
        
        case 'monthly':
          return date.getMonth() === now.getMonth() && 
                 date.getFullYear() === now.getFullYear();
        
        case 'quarterly': {
          const currentQuarter = Math.floor(now.getMonth() / 3);
          const itemQuarter = Math.floor(date.getMonth() / 3);
          return itemQuarter === currentQuarter && 
                 date.getFullYear() === now.getFullYear();
        }
        
        case 'yearly':
          return date.getFullYear() === now.getFullYear();
        
        default:
          return true;
      }
    });

    // Recalculate bestsellers from filtered data
    const summary = {};
    filteredOrders.forEach(item => {
      const productName = item.products?.productname || 'Unknown';
      const imageUrl = item.products?.image_url || '';

      if (!summary[productName]) {
        summary[productName] = {
          productname: productName,
          image_url: imageUrl,
          totalQuantity: 0,
          timesBought: new Set(),
        };
      }

      summary[productName].totalQuantity += item.quantity;
      summary[productName].timesBought.add(item.orderid);
    });

    return Object.values(summary)
      .map(item => ({
        ...item,
        timesBought: item.timesBought.size,
      }))
      .sort((a, b) => b.totalQuantity - a.totalQuantity);
  }, [orderData, timeFilter, bestsellers]);

  const handleFilterChange = (e) => {
    setTimeFilter(e.target.value);
  };

  return (
    <div className="bestseller-table-wrapper">
      <div className="bestseller-header">
        <h3>Bestseller Items</h3>
        <select 
          className="bestseller-filter" 
          value={timeFilter} 
          onChange={handleFilterChange}
        >
          <option value="all">All Time</option>
          <option value="today">Today</option>
          <option value="weekly">This Week</option>
          <option value="monthly">This Month</option>
          <option value="quarterly">This Quarter</option>
          <option value="yearly">This Year</option>
        </select>
      </div>
      <div className="table-scroll-box1">
        <table className="bestseller-table">
          <thead>
            <tr>
              <th></th>
              <th>Product Name</th>
              <th>Total Sold</th>
              <th>Orders</th>
            </tr>
          </thead>
          <tbody>
            {filteredBestsellers.length === 0 ? (
              <tr>
                <td colSpan="4" style={{ textAlign: 'center', color: '#999' }}>
                  No sales data for this period
                </td>
              </tr>
            ) : (
              filteredBestsellers.map((item, index) => (
                <tr key={index}>
                  <td className="product-image-cell">
                    <div className="image-wrapper">
                      {item.image_url ? (
                        <img
                          src={item.image_url}
                          alt={item.productname}
                          className="product-image"
                          onError={(e) => {
                            e.target.onerror = null;
                            e.target.src = '/placeholder-image.png';
                          }}
                        />
                      ) : (
                        <span className="no-image-text">Image</span>
                      )}
                    </div>
                  </td>
                  <td>{item.productname}</td>
                  <td>{item.totalQuantity}</td>
                  <td>{item.timesBought}</td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
};

export default Bestseller;