import React, { useState } from 'react';
import SalesReceipt from '../PointOfSales/SalesReceipt';
import '../stylecss/SalesSuccessModal.css';

const SalesSuccessModal = ({ isOpen, onClose, orderData }) => {
  const [showReceipt, setShowReceipt] = useState(false);

  if (!isOpen) return null;

  const handleViewReceipt = () => {
    setShowReceipt(true);
  };

  const handleCloseReceipt = () => {
    setShowReceipt(false);
  };

  const handleCloseAll = () => {
    setShowReceipt(false);
    onClose();
  };

  return (
    <>
      {/* Main Success Modal */}
      {!showReceipt && (
        <div className="success-modal-overlay">
          <div className="success-modal">
            {/* Modal Header */}
            <div className="success-modal-header">
              <div className={`success-icon ${orderData.status === 'COMPLETE' ? 'complete' : 'incomplete'}`}>
                <svg width="40" height="40" viewBox="0 0 24 24" fill="none">
                  <path d="M20 6L9 17L4 12" stroke="white" strokeWidth="3" strokeLinecap="round" strokeLinejoin="round"/>
                </svg>
              </div>
              <h3>Transaction Complete!</h3>
              <p className="success-subtitle">Order #{orderData.orderId} has been processed successfully</p>
            </div>

            {/* Modal Body */}
            <div className="success-modal-body">
              {/* Transaction Details */}
              <div className="transaction-details">
                <div className="detail-row">
                  <span className="detail-label">Order ID</span>
                  <span className="detail-value">#{orderData.orderId}</span>
                </div>
                <div className="detail-row">
                  <span className="detail-label">Items</span>
                  <span className="detail-value">{orderData.itemCount}</span>
                </div>
                <div className="detail-row">
                  <span className="detail-label">Total Amount</span>
                  <span className="detail-value">₱{orderData.totalAmount?.toFixed(2)}</span>
                </div>
                <div className="detail-row">
                  <span className="detail-label">Amount Paid</span>
                  <span className="detail-value">₱{orderData.amountPaid?.toFixed(2)}</span>
                </div>
                <div className="detail-row">
                  <span className="detail-label">Change</span>
                  <span className="detail-value change-amount">₱{orderData.change?.toFixed(2)}</span>
                </div>
                <div className="detail-row">
                  <span className="detail-label">Status</span>
                  <span className={`detail-value status-badge ${orderData.status === 'COMPLETE' ? 'complete' : 'incomplete'}`}>
                    {orderData.status}
                  </span>
                </div>
              </div>

              {/* Status Message */}
              <div className={`status-message ${orderData.status === 'COMPLETE' ? 'complete' : 'incomplete'}`}>
                {orderData.status === 'COMPLETE' 
                  ? '✓ Payment completed successfully' 
                  : '⚠ Partial payment received'}
              </div>
            </div>

            {/* Modal Footer */}
            <div className="success-modal-footer">
              <button className="success-view-receipt-btn" onClick={handleViewReceipt}>
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" style={{ marginRight: '8px' }}>
                  <path d="M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/>
                  <path d="M9 12h6M9 16h6" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/>
                </svg>
                View Receipt
              </button>
              <button className="success-close-btn" onClick={onClose}>
                Close
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Show Receipt Modal */}
      {showReceipt && (
        <SalesReceipt
          orderId={orderData.orderId}
          onClose={handleCloseReceipt}
        />
      )}
    </>
  );
};

export default SalesSuccessModal;