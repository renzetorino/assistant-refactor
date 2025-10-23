import React, { useState, useEffect, useRef } from 'react';
import { supabase } from '../supabase';
import '../stylecss/PointOfSales/SalesReceipt.css';

const SalesReceipt = ({ orderId, onClose }) => {
  const [receiptData, setReceiptData] = useState(null);
  const [loading, setLoading] = useState(true);
  const receiptRef = useRef(null);

  useEffect(() => {
    if (orderId) {
      fetchReceiptData();
    }
  }, [orderId]);

  const fetchReceiptData = async () => {
    try {
      // Fetch order details
      const { data: orderData, error: orderError } = await supabase
        .from('orders')
        .select('*')
        .eq('orderid', orderId)
        .single();

      if (orderError) throw orderError;

      // Fetch order items with product details
      const { data: itemsData, error: itemsError } = await supabase
        .from('orderitems')
        .select(`
          orderitemid,
          quantity,
          unitprice,
          subtotal,
          productid,
          productcategoryid,
          products (
            productname
          ),
          productcategory (
            color,
            agesize
          )
        `)
        .eq('orderid', orderId);

      if (itemsError) throw itemsError;

      setReceiptData({
        order: orderData,
        items: itemsData
      });
      setLoading(false);
    } catch (error) {
      console.error('Error fetching receipt data:', error);
      alert('Failed to load receipt data');
      setLoading(false);
    }
  };

  const formatDateTime = (dateTimeString) => {
    if (!dateTimeString) return { date: '', time: '' };
    const dateTime = new Date(dateTimeString);
    const date = dateTime.toLocaleDateString('en-US', { 
      year: 'numeric', 
      month: 'long', 
      day: 'numeric' 
    });
    const time = dateTime.toLocaleTimeString('en-US', { 
      hour: '2-digit', 
      minute: '2-digit',
      hour12: true 
    });
    return { date, time };
  };

  const handleDownload = async () => {
    try {
      // Load jsPDF from CDN if not already loaded
      if (!window.jspdf) {
        const script = document.createElement('script');
        script.src = 'https://cdnjs.cloudflare.com/ajax/libs/jspdf/2.5.1/jspdf.umd.min.js';
        document.head.appendChild(script);
        
        await new Promise((resolve, reject) => {
          script.onload = resolve;
          script.onerror = reject;
        });
      }

      const { jsPDF } = window.jspdf;
      const doc = new jsPDF({
        orientation: 'portrait',
        unit: 'mm',
        format: 'a4'
      });

      const { date, time } = formatDateTime(receiptData.order.orderdate);
      
      // Set font
      doc.setFont('courier');
      
      // Header - Business Name
      doc.setFontSize(20);
      doc.setFont('courier', 'bold');
      doc.text('BuiswAIz', 105, 20, { align: 'center' });
      
      // Order ID
      doc.setFontSize(14);
      doc.setFont('courier', 'normal');
      doc.text(`Order ID: #${orderId}`, 105, 30, { align: 'center' });
      
      // Info Section
      doc.setFontSize(10);
      let yPos = 45;
      doc.text('Address: 98 E. Santos St. Concepcion Uno Marikina City', 20, yPos);
      yPos += 6;
      doc.text('Phone No.: [To be provided]', 20, yPos);
      yPos += 6;
      doc.text(`Date: ${date}`, 20, yPos);
      yPos += 6;
      doc.text(`Time: ${time}`, 20, yPos);
      yPos += 10;

      // "Not official receipt" notice
      doc.setFont('courier', 'normal');
      doc.setFontSize(8);
      doc.text('- - - - - - - - - - - - This is not the official receipt - - - - - - - - - - - -', 105, yPos, { align: 'center' });
      yPos += 10;
      
      // Items Header
      doc.setFont('courier', 'bold');
      doc.setFontSize(9);
      doc.text('Product', 20, yPos);
      doc.text('Qty', 135, yPos, { align: 'right' });
      doc.text('Price', 160, yPos, { align: 'right' });
      doc.text('Item#', 185, yPos, { align: 'right' });
      yPos += 6;
      
      // Items
      doc.setFont('courier', 'normal');
      doc.setFontSize(9);
      
      receiptData.items.forEach((item) => {
        // Check if we need a new page
        if (yPos > 250) {
          doc.addPage();
          yPos = 20;
        }
        
        // Product name
        const productName = item.products?.productname || 'Unknown Product';
        doc.setFont('courier', 'bold');
        doc.text(productName, 20, yPos);
        yPos += 5;
        
        // Variant info
        if (item.productcategory?.agesize || item.productcategory?.color) {
          const variant = [item.productcategory?.agesize, item.productcategory?.color]
            .filter(Boolean)
            .join(' - ');
          doc.setFont('courier', 'normal');
          doc.setFontSize(8);
          doc.text(`(${variant})`, 20, yPos);
          yPos += 5;
        }
        
        // Quantity, Price, Item#
        doc.setFontSize(9);
        doc.text(item.quantity.toString(), 135, yPos - (item.productcategory?.agesize || item.productcategory?.color ? 5 : 0), { align: 'right' });
        doc.text(`P${item.unitprice.toFixed(2)}`, 160, yPos - (item.productcategory?.agesize || item.productcategory?.color ? 5 : 0), { align: 'right' });
        doc.text(item.orderitemid.toString(), 185, yPos - (item.productcategory?.agesize || item.productcategory?.color ? 5 : 0), { align: 'right' });
        
        yPos += 6;
      });
      
      yPos += 5;
      
      // Totals Section
      doc.setFont('courier', 'bold');
      doc.setFontSize(11);
      doc.text('TOTAL AMOUNT:', 20, yPos);
      doc.text(`P${receiptData.order.totalamount.toFixed(2)}`, 190, yPos, { align: 'right' });
      yPos += 8;
      
      doc.setFontSize(10);
      doc.setFont('courier', 'normal');
      doc.text('AMOUNT PAID:', 20, yPos);
      doc.text(`P${receiptData.order.amount_paid.toFixed(2)}`, 190, yPos, { align: 'right' });
      yPos += 7;
      
      doc.text('CHANGE:', 20, yPos);
      doc.text(`P${receiptData.order.change.toFixed(2)}`, 190, yPos, { align: 'right' });
      yPos += 12;
      
      doc.setFont('courier', 'bold');
      doc.setFontSize(9);
      doc.text('Signature:', 20, yPos);
      yPos += 20;
      
      doc.line(20, yPos, 100, yPos);
      yPos += 5;
      doc.setFont('courier', 'normal');
      doc.setFontSize(8);
      doc.text('[Signature]', 60, yPos, { align: 'center' });
      
      doc.save(`Receipt_Order_${orderId}.pdf`);
      
    } catch (error) {
      console.error('Error generating PDF:', error);
      alert('Failed to generate PDF. Please try again.');
    }
  };

  if (loading) {
    return (
      <div className="receipt-modal-overlay">
        <div className="loading-receipt">
          <div className="loading-spinner"></div>
          <p>Loading receipt...</p>
        </div>
      </div>
    );
  }

  if (!receiptData) {
    return null;
  }

  const { date, time } = formatDateTime(receiptData.order.orderdate);

  return (
    <div className="receipt-modal-overlay">
      <div className="receipt-modal-wrapper">
        <div className="receipt-modal-header">
          <h2 className="receipt-modal-title">Sales Receipt</h2>
        </div>

        <div className="receipt-modal-content">
          <div id="receipt-content" ref={receiptRef}>
            <div className="receipt-container">
              <div className="receipt-header">
                <div className="business-name">BuiswAIz</div>
                <div className="order-id">Order ID: #{orderId}</div>
              </div>

              <div className="info-section">
                <div><span className="info-label">Address:</span> 98 E. Santos St. Concepcion Uno Marikina City
</div>
                <div><span className="info-label">Phone No.:</span> [To be provided]</div>
                <div><span className="info-label">Date:</span> {date}</div>
                <div><span className="info-label">Time:</span> {time}</div>
              </div>
              <h5 className='not-off'>- - - - - - - - - - - - This is not the official receipt - - - - - - - - - - - -</h5>

              <div className="items-table">
                <div className="items-header">
                  <div>Product</div>
                  <div className="text-right">Qty</div>
                  <div className="text-right">Price</div>
                  <div className="text-right">Item#</div>
                </div>

                {receiptData.items.map((item, index) => (
                  <div key={index} className="item-row">
                    <div className="item-main">
                      <div>
                        <div className="item-name">{item.products?.productname || 'Unknown Product'}</div>
                        {(item.productcategory?.agesize || item.productcategory?.color) && (
                          <div className="item-variant">
                            ({[item.productcategory?.agesize, item.productcategory?.color].filter(Boolean).join(' - ')})
                          </div>
                        )}
                      </div>
                      <div className="text-right">{item.quantity}</div>
                      <div className="text-right">₱{item.unitprice.toFixed(2)}</div>
                      <div className="text-right">{item.orderitemid}</div>
                    </div>
                  </div>
                ))}
              </div>

              <div className="totals-section">
                <div className="total-row grand-total">
                  <span>TOTAL AMOUNT:</span>
                  <span>₱{receiptData.order.totalamount.toFixed(2)}</span>
                </div>
                <div className="total-row">
                  <span>AMOUNT PAID:</span>
                  <span>₱{receiptData.order.amount_paid.toFixed(2)}</span>
                </div>
                <div className="total-row">
                  <span>CHANGE:</span>
                  <span>₱{receiptData.order.change.toFixed(2)}</span>
                </div>
              </div>

              <div className="signature-section">
                <div className="signature-label">Signature:</div>
                <div className="signature-line">[Signature]</div>
              </div>
            </div>
          </div>

          <div className="receipt-actions">
            <button className="receipt-download-btn" onClick={handleDownload}>
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" style={{ marginRight: '8px' }}>
                <path d="M21 15v4a2 2 0 01-2 2H5a2 2 0 01-2-2v-4M7 10l5 5 5-5M12 15V3" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/>
              </svg>
              Download Receipt
            </button>
            <button className="receipt-cancel-btn" onClick={onClose}>
              Close
            </button>
          </div>
        </div>
      </div>
    </div>
  );
};

export default SalesReceipt;