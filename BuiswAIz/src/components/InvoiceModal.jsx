import React, { useState, useEffect, useCallback, useMemo } from 'react';
import jsPDF from 'jspdf';

const InvoiceModal = ({ invoice, onClose, onUpdateOrder }) => {
  const [showUpdateForm, setShowUpdateForm] = useState(false);
  const [updateData, setUpdateData] = useState({
    amountPaid: '',
    change: ''
  });
  const [errors, setErrors] = useState({});
  const [showSuccessModal, setShowSuccessModal] = useState(false);
  const [isUpdating, setIsUpdating] = useState(false);

  // Memoize calculations to prevent unnecessary recalculations
  const calculatedTotal = useMemo(() => {
    if (invoice.orderItems && invoice.orderItems.length > 0) {
      return invoice.totalOrderAmount || invoice.orderItems.reduce((sum, item) => sum + item.subtotal, 0);
    }
    return invoice.subtotal;
  }, [invoice.orderItems, invoice.totalOrderAmount, invoice.subtotal]);

  // Memoize amount paid calculation
  const amountPaid = useMemo(() => {
    if (invoice.orders?.amount_paid !== undefined && invoice.orders?.amount_paid !== null) {
      return invoice.orders.amount_paid;
    }
    if (invoice.amount_paid !== undefined && invoice.amount_paid !== null) {
      return invoice.amount_paid;
    }
    if (invoice.orderItems?.length > 0 && invoice.orderItems[0]?.orders?.amount_paid !== undefined) {
      return invoice.orderItems[0].orders.amount_paid;
    }
    return null;
  }, [invoice.orders, invoice.amount_paid, invoice.orderItems]);

  // Memoize change calculation
  const change = useMemo(() => {
    if (amountPaid === null || amountPaid === undefined) {
      return 0;
    }

    if (invoice.orders?.change !== undefined && invoice.orders?.change !== null) {
      return invoice.orders.change;
    }
    if (invoice.change !== undefined && invoice.change !== null) {
      return invoice.change;
    }
    if (invoice.orderItems?.length > 0 && invoice.orderItems[0]?.orders?.change !== undefined) {
      return invoice.orderItems[0].orders.change;
    }

    return amountPaid - calculatedTotal;
  }, [amountPaid, invoice.orders, invoice.change, invoice.orderItems, calculatedTotal]);

  // Memoize order status calculation
  const orderStatus = useMemo(() => {
    let status = '';
    
    if (invoice.orders?.orderstatus) {
      status = invoice.orders.orderstatus;
    } else if (invoice.orderstatus) {
      status = invoice.orderstatus;
    } else if (invoice.orderItems?.length > 0 && invoice.orderItems[0]?.orders?.orderstatus) {
      status = invoice.orderItems[0].orders.orderstatus;
    }

    const normalizedStatus = status.toUpperCase();
    if (normalizedStatus === 'COMPLETE' || normalizedStatus === 'INCOMPLETE') {
      return normalizedStatus;
    }
    
    return 'INCOMPLETE';
  }, [invoice.orders, invoice.orderstatus, invoice.orderItems]);

  // Memoize incomplete order check
  const isIncompleteOrder = useMemo(() => {
    return orderStatus === 'INCOMPLETE';
  }, [orderStatus]);

  // Fixed helper function to get product variant display
  const getVariantDisplay = useCallback((item) => {
    const variants = [];
    
    let color = null;
    let agesize = null;
    
    // Check multiple possible locations for variant data
    
    // Option 1: From productcategory nested object (most likely location for invoice items)
    if (item.productcategory?.color && item.productcategory.color.trim() !== '') {
      color = item.productcategory.color;
    }
    if (item.productcategory?.agesize && item.productcategory.agesize.trim() !== '') {
      agesize = item.productcategory.agesize;
    }
    
    // Option 2: Direct properties on item (for direct invoice data)
    if (!color && item.color && typeof item.color === 'string' && item.color.trim() !== '') {
      color = item.color;
    }
    if (!agesize && item.agesize && typeof item.agesize === 'string' && item.agesize.trim() !== '') {
      agesize = item.agesize;
    }
    
    // Build variants array with better formatting
    if (color) {
      variants.push(`${color}`);
    }
    if (agesize) {
      variants.push(`${agesize}`);
    }
    
    return variants.length > 0 ? `${variants.join(' - ')}` : '';
  }, []);

  // Initialize update form with current payment data
  useEffect(() => {
    if (showUpdateForm && invoice) {
      const currentAmountPaid = amountPaid || 0;
      setUpdateData({
        amountPaid: currentAmountPaid.toString(),
        change: ''
      });
    }
  }, [showUpdateForm, invoice, amountPaid]);

  // Auto-calculate change when amount paid changes with debouncing
  useEffect(() => {
    if (!showUpdateForm || !updateData.amountPaid) return;

    const timeoutId = setTimeout(() => {
      const paidAmount = parseFloat(updateData.amountPaid) || 0;
      const total = calculatedTotal;
      const calculatedChange = paidAmount - total;
      
      setUpdateData(prev => ({
        ...prev,
        change: calculatedChange >= 0 ? calculatedChange.toFixed(2) : '0.00'
      }));
    }, 100); // 100ms debounce

    return () => clearTimeout(timeoutId);
  }, [updateData.amountPaid, showUpdateForm, calculatedTotal]);

  // Optimize date and time formatting
  const formatDateTime = useCallback((timestamp) => {
    return new Date(timestamp).toLocaleString('en-US', {
      year: 'numeric',
      month: 'long',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
      hour12: true
    });
  }, []);

  // Format date only (for PDF)
  const formatDate = useCallback((timestamp) => {
    return new Date(timestamp).toLocaleDateString('en-US', {
      year: 'numeric',
      month: 'long',
      day: 'numeric',
    });
  }, []);

  // Format time only (for PDF)
  const formatTime = useCallback((timestamp) => {
    return new Date(timestamp).toLocaleTimeString('en-US', {
      hour: '2-digit',
      minute: '2-digit',
      hour12: true
    });
  }, []);

  // Optimize currency formatting
  const formatCurrency = useCallback((amount) => {
    return amount.toFixed(2).replace(/\B(?=(\d{3})+(?!\d))/g, ',');
  }, []);

  // Enhanced PDF download with proper variant display and time
  const handleDownloadPDF = useCallback(() => {
    const generatePDF = () => {
      try {
        const doc = new jsPDF();
        const timestamp = invoice.createdat || invoice.orderItems?.[0]?.orders?.orderdate || new Date();
        const date = formatDate(timestamp);
        const time = formatTime(timestamp);
        const orderId = invoice.orderid || invoice.orders?.orderid || 'N/A';

        // Header: BUISWAIZ on left, Order No. on right
        doc.setFontSize(12);
        doc.setFont(undefined, 'normal');
        doc.text('BuiswAlz', 20, 20);
        doc.text(`Order No. ${orderId}`, 190, 20, { align: 'right' });
        
        // Horizontal line under header
        doc.line(20, 25, 190, 25);
        
        // INVOICE title
        doc.setFontSize(24);
        doc.setFont(undefined, 'bold');
        doc.text('INVOICE', 20, 40);
        
        // Store Name (NEW)
        doc.setFontSize(11);
        doc.setFont(undefined, 'bold');
        doc.text(`Store Name:`, 20, 52);
        doc.setFont(undefined, 'normal');
        doc.text('ArtehKo', 48, 52);
        
        // Time and Date
        doc.setFont(undefined, 'bold');
        doc.text(`Time and Date:`, 20, 60);
        doc.setFont(undefined, 'normal');
        doc.text(`${date} ${time}`, 55, 60);
        
        // Status
        doc.setFont(undefined, 'bold');
        doc.text('Status:', 20, 68);
        doc.setFont(undefined, 'normal');
        doc.text(orderStatus, 38, 68);

        doc.setFont(undefined, 'bold');
        doc.text('Store Location:', 20, 76);
        doc.setFont(undefined, 'normal');
        doc.text('98 E. Santos St. Concepcion Uno Marikina City', 50, 76);
        
        // Items table header
        let yPosition = 91;
        doc.setFontSize(9);
        doc.setFont(undefined, 'bold');
        doc.setFillColor(240, 240, 240);
        doc.rect(20, yPosition - 5, 170, 8, 'F');
        
        doc.text('PRODUCT NAME', 22, yPosition);
        doc.text('CATEGORIES', 70, yPosition);
        doc.text('QUANTITY', 110, yPosition);
        doc.text('UNIT PRICE', 135, yPosition);
        doc.text('SUBTOTAL', 165, yPosition);
        
        // Items list
        yPosition += 10;
        doc.setFont(undefined, 'normal');
        let totalAmount = 0;
        
        if (invoice.orderItems && invoice.orderItems.length > 0) {
          invoice.orderItems.forEach((item, index) => {
            const productName = item.products?.productname || 'N/A';
            const variantInfo = getVariantDisplay(item);
            
            doc.text(productName, 22, yPosition);
            doc.text(variantInfo || '-', 70, yPosition);
            doc.text(item.quantity.toString(), 110, yPosition);
            doc.text(item.unitprice.toString(), 135, yPosition);
            doc.text(item.subtotal.toString(), 165, yPosition);
            
            yPosition += 8;
            totalAmount += item.subtotal;
          });
          
          totalAmount = invoice.totalOrderAmount || totalAmount;
        } else {
          const productName = invoice.products?.productname || 'N/A';
          const variantInfo = getVariantDisplay(invoice);
          
          doc.text(productName, 22, yPosition);
          doc.text(variantInfo || '-', 70, yPosition);
          doc.text(invoice.quantity.toString(), 110, yPosition);
          doc.text(invoice.unitprice.toString(), 135, yPosition);
          doc.text(invoice.subtotal.toString(), 165, yPosition);
          yPosition += 8;
          totalAmount = invoice.subtotal;
        }
        
        // Totals section
        yPosition += 10;
        doc.setFont(undefined, 'bold');
        doc.text('Total Amount', 135, yPosition, { align: 'right' });
        doc.text(totalAmount.toString(), 165, yPosition);

        // Footer
        yPosition += 25;
        doc.setFontSize(10);
        doc.setFont(undefined, 'normal');
        doc.text('Thank you for your business!', 20, yPosition);
        doc.text('Signature:', 135, yPosition);

        doc.save(`Invoice_${orderId}.pdf`);
      } catch (error) {
        console.error('Error generating PDF:', error);
        alert('Error generating PDF. Please try again.');
      }
    };

    // Use requestIdleCallback for better performance, fallback to setTimeout
    if ('requestIdleCallback' in window) {
      requestIdleCallback(generatePDF, { timeout: 1000 });
    } else {
      setTimeout(generatePDF, 0);
    }
  }, [invoice, formatDate, formatTime, formatCurrency, orderStatus, getVariantDisplay]);

  // Optimize form handlers with useCallback and debouncing
  const handleUpdateDataChange = useCallback((e) => {
    const { name, value } = e.target;
    
    // Use requestAnimationFrame for smoother updates
    requestAnimationFrame(() => {
      setUpdateData(prev => ({
        ...prev,
        [name]: value
      }));

      if (errors[name]) {
        setErrors(prev => ({
          ...prev,
          [name]: ''
        }));
      }
    });
  }, [errors]);

  const validateUpdateForm = useCallback(() => {
    const newErrors = {};
    const total = calculatedTotal;
    const paidAmount = parseFloat(updateData.amountPaid) || 0;

    if (!updateData.amountPaid || paidAmount < 0) {
      newErrors.amountPaid = 'Amount paid must be a positive number';
    } else if (paidAmount < total) {
      newErrors.amountPaid = 'Amount paid cannot be less than total amount to complete the order';
    }

    setErrors(newErrors);
    return Object.keys(newErrors).length === 0;
  }, [calculatedTotal, updateData.amountPaid]);

  const handleUpdateSubmit = useCallback(async () => {
    if (validateUpdateForm()) {
      setIsUpdating(true);
      
      try {
        const updateOrderData = {
          orderid: invoice.orderid,
          amountPaid: parseFloat(updateData.amountPaid),
          change: parseFloat(updateData.change),
          orderStatus: 'COMPLETE'
        };

        if (onUpdateOrder) {
          await onUpdateOrder(updateOrderData);
        }
        
        setShowUpdateForm(false);
        setShowSuccessModal(true);
        
      } catch (error) {
        console.error('Error updating order:', error);
        alert('Failed to update order. Please try again.');
      } finally {
        setIsUpdating(false);
      }
    }
  }, [validateUpdateForm, invoice.orderid, updateData, onUpdateOrder]);

  // Optimize event handlers with passive event listeners where possible
  const handleCancelUpdate = useCallback(() => {
    requestAnimationFrame(() => {
      setShowUpdateForm(false);
      setUpdateData({
        amountPaid: '',
        change: ''
      });
      setErrors({});
    });
  }, []);

  const handleSuccessModalClose = useCallback(() => {
    requestAnimationFrame(() => {
      setShowSuccessModal(false);
      onClose();
    });
  }, [onClose]);

  const handleShowUpdateForm = useCallback(() => {
    requestAnimationFrame(() => {
      setShowUpdateForm(true);
    });
  }, []);

  // Optimize modal close with event delegation
  const handleModalOverlayClick = useCallback((e) => {
    if (e.target === e.currentTarget) {
      requestAnimationFrame(() => {
        onClose();
      });
    }
  }, [onClose]);

  const handleModalContentClick = useCallback((e) => {
    e.stopPropagation();
  }, []);

  return (
    <div className="modal-overlay" onClick={handleModalOverlayClick}>
      <div className="invoice-modal-content" onClick={handleModalContentClick}>
        <div className="invoice-modal-inner">
          
          {!showUpdateForm ? (
            <div className="invoice-details-new">
              {/* Header */}
              <div className="invoice-header-new">
                <div className="invoice-brand">BuiswAlz</div>
                <div className="invoice-order-no">
                  <span>Order No. {invoice.orderid || invoice.orders?.orderid || 'N/A'}</span>
                  {isIncompleteOrder && (
                    <button 
                      className="complete-order-btn-inline"
                      onClick={handleShowUpdateForm}
                      title="Update incomplete order"
                    >
                      Complete Order
                    </button>
                  )}
                </div>
              </div>

              <div className="invoice-divider"></div>

              {/* Invoice Title */}
              <h1 className="invoice-title-new">INVOICE</h1>

              {/* Store Name, Time, Date, and Status */}
              <div className="invoice-meta-new">
                <div className="meta-item">
                  <strong>Store Name:</strong> ArtehKo
                </div>
                <div className="meta-item">
                  <strong>Time and Date:</strong> {formatDateTime(invoice.orderdate || invoice.orderItems?.[0]?.orders?.orderdate || new Date())}
                </div>
                <div className="meta-item">
                  <strong>Status:</strong> <span className={`status-badge-new ${orderStatus.toLowerCase()}`}>{orderStatus}</span>
                </div>
                <div className="meta-item">
                  <strong>Store Location:</strong> 98 E. Santos St. Concepcion Uno Marikina City
                </div>
              </div>

              {/* Items Table */}
              <div className="invoice-table-new">
                <table>
                  <thead>
                    <tr>
                      <th>PRODUCT NAME</th>
                      <th>CATEGORIES</th>
                      <th>QUANTITY</th>
                      <th>UNIT PRICE</th>
                      <th>SUBTOTAL</th>
                    </tr>
                  </thead>
                  <tbody>
                    {invoice.orderItems && invoice.orderItems.length > 0 ? (
                      invoice.orderItems.map((item, index) => {
                        const variantDisplay = getVariantDisplay(item);
                        
                        return (
                          <tr key={index}>
                            <td>{item.products?.productname || 'N/A'}</td>
                            <td>{variantDisplay || '-'}</td>
                            <td>{item.quantity}</td>
                            <td>P{item.unitprice}</td>
                            <td>P{item.subtotal}</td>
                          </tr>
                        );
                      })
                    ) : (
                      <tr>
                        <td>{invoice.products?.productname || 'N/A'}</td>
                        <td>{getVariantDisplay(invoice) || '-'}</td>
                        <td>{invoice.quantity}</td>
                        <td>{invoice.unitprice}</td>
                        <td>{invoice.subtotal}</td>
                      </tr>
                    )}
                  </tbody>
                </table>
              </div>

              <div className="invoice-total-section">
                <div className="total-amount-row-new">
                  <span className="total-label">Total Amount</span>
                  <span className="total-value">P{calculatedTotal}</span>
                </div>
              </div>

              {/* Footer */}
              <div className="invoice-footer-new">
                <div className="footer-text">Thank you for your business!</div>
                <div className="footer-signature">Signature:</div>
              </div>

              <div className="modal-footer">
                <button className="download-pdf-btn" onClick={handleDownloadPDF}>
                  Download PDF
                </button>
                <button className="close-btn1" onClick={onClose}>
                  Close
                </button>
              </div>
            </div>
          ) : (
            /* Update Form */
            <div className="update-form-section">
              <h4>Update Order Payment</h4>
              
              <div className="update-form">
                <div className="form-group">
                  <label htmlFor="updateAmountPaid">Amount Paid *</label>
                  <input
                    type="number"
                    id="updateAmountPaid"
                    name="amountPaid"
                    value={updateData.amountPaid}
                    onChange={handleUpdateDataChange}
                    className={`form-input ${errors.amountPaid ? 'error' : ''}`}
                    placeholder="Enter amount paid"
                    min="0"
                    step="0.01"
                    disabled={isUpdating}
                  />
                  {errors.amountPaid && <span className="error-message">{errors.amountPaid}</span>}
                </div>

                <div className="form-group">
                  <label htmlFor="updateChange">Change</label>
                  <input
                    type="number"
                    id="updateChange"
                    name="change"
                    value={updateData.change}
                    className="form-input readonly"
                    placeholder="Auto-calculated"
                    readOnly
                  />
                </div>

                <div className="payment-summary-section">
                  <div className="payment-summary">
                    <div className="summary-row total-row">
                      <span className="summary-label">Total Amount:</span>
                      <span className="summary-value total-amount">₱{calculatedTotal.toLocaleString(undefined, {minimumFractionDigits: 2, maximumFractionDigits: 2})}</span>
                    </div>
                    <div className="summary-row change-row">
                      <span className="summary-label">New Change:</span>
                      <span className="summary-value change-amount">
                        {updateData.change ? `₱${parseFloat(updateData.change).toLocaleString(undefined, {minimumFractionDigits: 2, maximumFractionDigits: 2})}` : '₱0.00'}
                      </span>
                    </div>
                  </div>
                </div>
              </div>

              <div className="modal-footer">
                <button 
                  className="cancel-btn" 
                  onClick={handleCancelUpdate}
                  disabled={isUpdating}
                >
                  Cancel
                </button>
                <button 
                  className="save-btn" 
                  onClick={handleUpdateSubmit}
                  disabled={isUpdating}
                >
                  {isUpdating ? 'Completing Order...' : 'Complete Order'}
                </button>
              </div>
            </div>
          )}
        </div>

        {/* Success Modal */}
        {showSuccessModal && (
          <div className="success-overlay">
            <div className="success-modal">
              <div className="success-header">
                <h3>Order Completed</h3>
              </div>
              <div className="success-body">
                <p>Order <strong>{invoice.orderid || invoice.orders?.orderid || 'N/A'}</strong> has been successfully completed!</p>
                <div className="success-details">
                  <div className="success-detail-row">
                    <span>Amount Paid:</span>
                    <span>₱{parseFloat(updateData.amountPaid).toLocaleString(undefined, {minimumFractionDigits: 2, maximumFractionDigits: 2})}</span>
                  </div>
                  <div className="success-detail-row">
                    <span>Change:</span>
                    <span>₱{parseFloat(updateData.change).toLocaleString(undefined, {minimumFractionDigits: 2, maximumFractionDigits: 2})}</span>
                  </div>
                </div>
              </div>
              <div className="success-footer">
                <button 
                  className="success-ok-btn" 
                  onClick={handleSuccessModalClose}
                >
                  OK
                </button>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );
};

export default InvoiceModal;