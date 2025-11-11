import React, { useState } from 'react';
import "../stylecss/Dashboard/TopSellingProducts.css";

// ⬇️ 1. Add imports for the new component and its template function
import UploadInventory from '../components/UploadInventory';
import { downloadInventoryTemplate } from '../components/UploadInventory';

const TopSellingProducts = ({ topSellingProducts, leastSellingProducts, notSellingProducts }) => {
  const [selectedView, setSelectedView] = useState('top');
  
  // ⬇️ 2. Add state for the inventory modal
  const [showInventoryModal, setShowInventoryModal] = useState(false);

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
    // ⬇️ 3. Wrap in a React Fragment to allow the modal to be a sibling
    <>
      <div className="top-selling-container">
      <div className="headers-pp">
        <h3> Products Performance</h3>
        
        {/* ⬇️ 4. Add a button to open the modal */}
        {/* You may want to style this button to match your project */}
        <button 
          onClick={() => setShowInventoryModal(true)} 
          className="btn" 
          style={{marginLeft: '1rem'}}
        >
          Upload Inventory
        </button>

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

      {/* ⬇️ 5. Add the Inventory Upload Modal JSX */}
      {showInventoryModal && (
        <div className="modal-overlay" onClick={() => setShowInventoryModal(false)}>
          <div className="modal-sheet" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <h2>Upload Inventory Spreadsheet</h2>
              <button
                className="close-btn"
                onClick={() => setShowInventoryModal(false)}
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
                onClick={downloadInventoryTemplate} // <-- Uses inventory template function
                aria-label="Download inventory upload template"
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
                {/* Updated column names for inventory */}
                Expected columns:&nbsp;
                <code>productname</code>, <code>suppliername</code>, <code>cost</code>,
                <code> price</code>, <code>stock</code>, <code>description</code>,
                <code> color</code>, <code>agesize</code>, <code>reorderpoint</code>
              </div>
            </div>

            {/* Uses the new UploadInventory component */}
            <UploadInventory /> 

            <div className="modal-actions">
              <button onClick={() => setShowInventoryModal(false)}>Close</button>
            </div>
          </div>
        </div>
      )}
    </>
  );
};

export default TopSellingProducts;