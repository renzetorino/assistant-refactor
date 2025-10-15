import React, { useState, useEffect } from 'react';
import '../stylecss/PointOfSales/SellingPanel.css';

const SellingPanel = ({ 
  cart, 
  amountPaid, 
  setAmountPaid, 
  onUpdateQuantity, 
  onRemoveFromCart, 
  onCompleteTransaction, 
  onClearCart 
}) => {
  const [orderDate, setOrderDate] = useState('');
  const [orderTime, setOrderTime] = useState('');

  // Initialize date and time when component mounts
  useEffect(() => {
    const now = new Date();
    const year = now.getFullYear();
    const month = String(now.getMonth() + 1).padStart(2, '0');
    const day = String(now.getDate()).padStart(2, '0');
    const hours = String(now.getHours()).padStart(2, '0');
    const minutes = String(now.getMinutes()).padStart(2, '0');
    
    setOrderDate(`${year}-${month}-${day}`);
    setOrderTime(`${hours}:${minutes}`);
  }, []);

  const subtotal = cart.reduce((sum, item) => sum + (item.price * item.quantity), 0);
  const total = subtotal;
  const change = amountPaid ? (parseFloat(amountPaid) - total) : 0;

  const handleCompleteTransaction = () => {
    if (!orderDate || !orderTime) {
      alert('Please select date and time for the transaction.');
      return;
    }
    onCompleteTransaction({ orderDate, orderTime });
  };

  const handleClearCart = () => {
    onClearCart();
    // Reset date and time to current when clearing cart
    const now = new Date();
    const year = now.getFullYear();
    const month = String(now.getMonth() + 1).padStart(2, '0');
    const day = String(now.getDate()).padStart(2, '0');
    const hours = String(now.getHours()).padStart(2, '0');
    const minutes = String(now.getMinutes()).padStart(2, '0');
    
    setOrderDate(`${year}-${month}-${day}`);
    setOrderTime(`${hours}:${minutes}`);
  };

  return (
    <div className="pos-order-section">
      <div className="order-header">
        <h2>Current Order</h2>
        <span className="order-count">{cart.length} items</span>
      </div>

      <div className="order-items">
        {cart.length === 0 ? (
          <div className="empty-order">
            <p>No items in order</p>
            <small>Select products to add</small>
          </div>
        ) : (
          cart.map(item => (
            <div key={item.productcategoryid} className="order-item">
              <div className="order-item-header">
                <div className="order-item-info">
                  <h4>{item.productname}</h4>
                  {(item.color || item.agesize) && (
                    <p className="order-item-variant">
                      {[item.color, item.agesize].filter(Boolean).join(' • ')}
                    </p>
                  )}
                  <p className="order-item-price">₱{item.price.toFixed(2)} each</p>
                </div>
                <div className="order-item-total">
                  ₱{(item.price * item.quantity).toFixed(2)}
                </div>
              </div>
              <div className="order-item-footer">
                <div className="order-item-controls">
                  <button
                    className="qty-btn"
                    onClick={() => onUpdateQuantity(item.productcategoryid, item.quantity - 1)}
                  >
                    −
                  </button>
                  <span className="qty-display">{item.quantity}</span>
                  <button
                    className="qty-btn"
                    onClick={() => onUpdateQuantity(item.productcategoryid, item.quantity + 1)}
                  >
                    +
                  </button>
                </div>
                <button
                  className="remove-btn1"
                  onClick={() => onRemoveFromCart(item.productcategoryid)}
                >
                  🗑️
                </button>
              </div>
            </div>
          ))
        )}
      </div>

      {cart.length > 0 && (
        <div className="order-summary">
          {/* Date and Time Section */}
          <div className="datetime-section">
            <div className="datetime-group">
              <label htmlFor="orderDate">Date</label>
              <input
                type="date"
                id="orderDate"
                value={orderDate}
                onChange={(e) => setOrderDate(e.target.value)}
                className="datetime-input"
              />
            </div>
            <div className="datetime-group">
              <label htmlFor="orderTime">Time</label>
              <input
                type="time"
                id="orderTime"
                value={orderTime}
                onChange={(e) => setOrderTime(e.target.value)}
                className="datetime-input"
              />
            </div>
          </div>

          {/* Summary Grid */}
          <div className="summary-grid">
            <div className="summary-row1">
              <label>Subtotal</label>
              <input type="text" readOnly value={`₱${subtotal.toFixed(2)}`} />
            </div>
            <div className="summary-row1">
              <label>Total</label>
              <input type="text" readOnly value={`₱${total.toFixed(2)}`} />
            </div>
            <div className="summary-row1">
              <label>Amount</label>
              <input
                type="number"
                value={amountPaid}
                onChange={(e) => {
                  const value = e.target.value;
                  if (value === '' || parseFloat(value) >= 0) {
                    setAmountPaid(value);
                  }
                }}
                onKeyDown={(e) => {
                  if (e.key === '-' || e.key === '+' || e.key === 'e' || e.key === 'E') {
                    e.preventDefault();
                  }
                }}
                placeholder="0.00"
                min="0"
                step="0.01"
              />
            </div>
            <div className="summary-row1">
              <label>Change</label>
              <input 
                type="text" 
                readOnly 
                value={amountPaid && parseFloat(amountPaid) > 0 ? `₱${change.toFixed(2)}` : '₱0.00'}
                className={change < 0 ? 'change-negative' : 'change-positive'}
              />
            </div>
          </div>

          {/* Button Grid */}
          <div className="button-grid">
            <button className="checkout-btn" onClick={handleCompleteTransaction}>
              Complete Transaction
            </button>
            <button className="clear-btn" onClick={handleClearCart}>
              Clear Orders
            </button>
          </div>
        </div>
      )}
    </div>
  );
};

export default SellingPanel;