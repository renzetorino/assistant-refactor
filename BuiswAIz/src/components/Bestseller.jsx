import React, { useMemo } from 'react';

const Bestseller = ({ orderData }) => {
  // Calculate bestsellers from filtered orderData
  const filteredBestsellers = useMemo(() => {
    if (!orderData || orderData.length === 0) return [];
    
    const summary = {};
    orderData.forEach(item => {
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
  }, [orderData]);

  return (
    <div className="bestseller-table-wrapper">
      <div className="bestseller-header">
        <h3>Bestseller Items</h3>
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
                <td colSpan="4" style={{ 
                  textAlign: 'center', 
                  padding: '40px 20px',
                  color: '#6b7280'
                }}>
                  <div>
                    <div style={{ 
                      fontWeight: '600', 
                      fontSize: '16px',
                      marginBottom: '4px'
                    }}>
                      No Sales Data Available
                    </div>
                    <div style={{ 
                      fontSize: '14px',
                      color: '#9ca3af'
                    }}>
                      There are no sold items for the selected period
                    </div>
                  </div>
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