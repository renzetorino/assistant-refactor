import React, { useState } from 'react';
import "../stylecss/Dashboard/TopSellingProducts.css";

const TopSellingProducts = ({ topSellingProducts, leastSellingProducts, notSellingProducts }) => {
  const [selectedView, setSelectedView] = useState('top');

  const getCurrentProducts = () => {
    switch (selectedView) {
      case 'top':
        return topSellingProducts;
      case 'least':
        return leastSellingProducts;
      case 'not':
        return notSellingProducts;
      default:
        return topSellingProducts;
    }
  };

  const currentProducts = getCurrentProducts();

  const getEmptyMessage = () => {
    switch (selectedView) {
      case 'top':
        return 'No sales data available';
      case 'least':
        return 'No least selling products available';
      case 'not':
        return 'All products have sales!';
      default:
        return 'No data available';
    }
  };

  return (
    <div className="top-selling-container">
    <div className="headers-pp">
      <h3> Products Performance</h3>
      <div className="mydict">
        <div>
          <label>
            <input 
              type="radio" 
              name="radio" 
              checked={selectedView === 'top'}
              onChange={() => setSelectedView('top')}
            />
            <span>Top Selling</span>
          </label>
          <label>
            <input 
              type="radio" 
              name="radio"
              checked={selectedView === 'least'}
              onChange={() => setSelectedView('least')}
            />
            <span>Least Selling</span>
          </label>
          <label>
            <input 
              type="radio" 
              name="radio"
              checked={selectedView === 'not'}
              onChange={() => setSelectedView('not')}
            />
            <span>Not Selling</span>
          </label>
        </div>
      </div>
    </div>

      <div className="top-selling-scrollable">
        <div className="top-selling-scroll-container">
          {currentProducts?.map((item, index) => (
            <div key={index} className="top-selling-item">
              <div className="product-rank">#{index + 1}</div>
              <div className="product-image-container">
                {item.image_url ? (
                  <img
                    src={item.image_url}
                    alt={item.productname}
                    className="product-image-small"
                    onError={(e) => {
                      e.target.onerror = null;
                      e.target.src = '/placeholder-image.png';
                    }}
                  />
                ) : (
                  <div className="image-placeholder-small">
                    <span>No Image</span>
                  </div>
                )}
              </div>
              <div className="product-details">
                <div className="product-name-small">{item.productname}</div>
                <div className="product-sales">
                  <span className="sales-quantity">
                    {selectedView === 'not' ? '0 sold' : `${item.totalQuantity} sold`}
                  </span>
                  {item.timesBought !== undefined && (
                    <span className="times-bought">
                      {selectedView === 'not' ? '0 orders' : `${item.timesBought} orders`}
                    </span>
                  )}
                </div>
              </div>
            </div>
          ))}
          {(!currentProducts || currentProducts.length === 0) && (
            <div className="empty-state-scroll">
              <p>{getEmptyMessage()}</p>
            </div>
          )}
        </div>
      </div>
    </div>
  );
};

export default TopSellingProducts;