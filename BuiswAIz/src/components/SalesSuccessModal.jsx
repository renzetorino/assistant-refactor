import React from 'react';
import '../stylecss/SalesSuccessModal.css';

const SalesSuccessModal = ({ 
  isOpen, 
  onClose, 
  orderData = {} 
}) => {
  if (!isOpen) return null;

  const {
    orderId = 'N/A',
    totalAmount = 0,
    amountPaid = 0,
    change = 0,
    status = 'COMPLETE',
    itemCount = 1
  } = orderData;

  return (
    <div className="success-modal-overlay">
      <div className="success-modal">
        <div className="success-modal-header">
          <div className={`success-icon ${status.toLowerCase()}`}>
            {status === 'COMPLETE' ? '✓' : '!'}
          </div>
          <h3>Successfully Added Sale!</h3>
        </div>
        
        <div className="success-modal-body">
          <div className="transaction-details">
            <div className="detail-row">
              <span className="detail-label">Order ID:</span>
              <span className="detail-value">{orderId}</span>
            </div>
            <div className="detail-row">
              <span className="detail-label">Items:</span>
              <span className="detail-value">{itemCount} product{itemCount > 1 ? 's' : ''}</span>
            </div>
            <div className="detail-row">
              <span className="detail-label">Total Amount:</span>
              <span className="detail-value">₱{totalAmount.toLocaleString(undefined, {minimumFractionDigits: 2, maximumFractionDigits: 2})}</span>
            </div>
            <div className="detail-row">
              <span className="detail-label">Amount Paid:</span>
              <span className="detail-value">₱{amountPaid.toLocaleString(undefined, {minimumFractionDigits: 2, maximumFractionDigits: 2})}</span>
            </div>
            <div className="detail-row">
              <span className="detail-label">Change:</span>
              <span className="detail-value">₱{change.toLocaleString(undefined, {minimumFractionDigits: 2, maximumFractionDigits: 2})}</span>
            </div>
            <div className="detail-row">
              <span className="detail-label">Status:</span>
              <span className="detail-value">{status}</span>
            </div>
          </div>
          
          <div className={`status-message ${status.toLowerCase()}`}>
            {status === 'COMPLETE' 
              ? 'Payment completed successfully!' 
              : 'Order saved as incomplete - partial payment received.'}
          </div>
        </div>
        
        <div className="success-modal-footer">
          <button 
            type="button" 
            className="success-ok-btn"
            onClick={onClose}
          >
            OK
          </button>
        </div>
      </div>
    </div>
  );
};

export default SalesSuccessModal;