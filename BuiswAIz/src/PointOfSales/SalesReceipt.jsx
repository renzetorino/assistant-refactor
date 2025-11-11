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
      
      // 80mm width thermal paper (3.15 inches)
      const pageWidth = 80; // mm
      const margin = 5; // mm
      const contentWidth = pageWidth - (margin * 2);
      
      // Calculate initial height (will add pages if needed)
      let currentHeight = 10;
      
      // Calculate approximate final height first
      let estimatedHeight = 100; // Base height for header, footer, etc.
      estimatedHeight += receiptData.items.length * 20; // Approximate per item
      const finalHeight = Math.max(Math.min(estimatedHeight, 300), 200); // Between 200-300mm
      
      // Create document with custom dimensions
      const doc = new jsPDF({
        orientation: 'portrait',
        unit: 'mm',
        format: [pageWidth, finalHeight]
      });

      const { date, time } = formatDateTime(receiptData.order.orderdate);
      
      // Set font
      doc.setFont('courier');
      
      // Header - Business Name
      doc.setFontSize(14);
      doc.setFont('courier', 'bold');
      doc.text('BuiswAIz', pageWidth / 2, currentHeight, { align: 'center' });
      currentHeight += 7;
      
      // Order ID
      doc.setFontSize(10);
      doc.setFont('courier', 'normal');
      doc.text(`Order ID: #${orderId}`, pageWidth / 2, currentHeight, { align: 'center' });
      currentHeight += 8;
      
      // Info Section
      doc.setFontSize(8);
      doc.text('Address:', margin, currentHeight);
      currentHeight += 4;
      doc.text('98 E. Santos St.', margin, currentHeight);
      currentHeight += 4;
      doc.text('Concepcion Uno', margin, currentHeight);
      currentHeight += 4;
      doc.text('Marikina City', margin, currentHeight);
      currentHeight += 5;
      
      doc.text(`Date: ${date}`, margin, currentHeight);
      currentHeight += 4;
      doc.text(`Time: ${time}`, margin, currentHeight);
      currentHeight += 7;

      // Separator line
      doc.setLineWidth(0.3);
      doc.line(margin, currentHeight, pageWidth - margin, currentHeight);
      currentHeight += 5;

      // "Not official receipt" notice
      doc.setFont('courier', 'normal');
      doc.setFontSize(7);
      const noticeText = 'This is not the official receipt';
      doc.text(noticeText, pageWidth / 2, currentHeight, { align: 'center' });
      currentHeight += 5;
      
      // Separator line
      doc.line(margin, currentHeight, pageWidth - margin, currentHeight);
      currentHeight += 5;
      
      // Items Header
      doc.setFont('courier', 'bold');
      doc.setFontSize(8);
      doc.text('Item', margin, currentHeight);
      doc.text('Qty', pageWidth - margin - 25, currentHeight, { align: 'left' });
      doc.text('Price', pageWidth - margin, currentHeight, { align: 'right' });
      currentHeight += 4;
      
      // Separator line
      doc.setLineWidth(0.1);
      doc.line(margin, currentHeight, pageWidth - margin, currentHeight);
      currentHeight += 4;
      
      // Items
      doc.setFont('courier', 'normal');
      doc.setFontSize(8);
      
      receiptData.items.forEach((item, index) => {
        // Product name (wrap if too long)
        const productName = item.products?.productname || 'Unknown Product';
        const maxWidth = contentWidth - 5;
        const lines = doc.splitTextToSize(productName, maxWidth);
        
        lines.forEach((line) => {
          doc.text(line, margin, currentHeight);
          currentHeight += 4;
        });
        
        // Variant info
        if (item.productcategory?.agesize || item.productcategory?.color) {
          const variant = [item.productcategory?.agesize, item.productcategory?.color]
            .filter(Boolean)
            .join(' - ');
          doc.setFontSize(7);
          doc.text(`(${variant})`, margin + 2, currentHeight);
          currentHeight += 4;
          doc.setFontSize(8);
        }
        
        // Quantity and Price on same line
        const qtyY = currentHeight;
        doc.text(`${item.quantity}x`, pageWidth - margin - 25, qtyY);
        doc.text(`P${item.unitprice.toFixed(2)}`, pageWidth - margin, qtyY, { align: 'right' });
        currentHeight += 4;
        
        // Subtotal
        doc.setFont('courier', 'bold');
        doc.text(`P${item.subtotal.toFixed(2)}`, pageWidth - margin, currentHeight, { align: 'right' });
        doc.setFont('courier', 'normal');
        currentHeight += 5;
        
        // Add space between items
        if (index < receiptData.items.length - 1) {
          currentHeight += 2;
        }
      });
      
      currentHeight += 3;
      
      // Separator line
      doc.setLineWidth(0.3);
      doc.line(margin, currentHeight, pageWidth - margin, currentHeight);
      currentHeight += 5;
      
      // Totals Section
      doc.setFont('courier', 'bold');
      doc.setFontSize(9);
      doc.text('TOTAL:', margin, currentHeight);
      doc.text(`P${receiptData.order.totalamount.toFixed(2)}`, pageWidth - margin, currentHeight, { align: 'right' });
      currentHeight += 6;
      
      doc.setFontSize(8);
      doc.setFont('courier', 'normal');
      doc.text('PAID:', margin, currentHeight);
      doc.text(`P${receiptData.order.amount_paid.toFixed(2)}`, pageWidth - margin, currentHeight, { align: 'right' });
      currentHeight += 5;
      
      doc.text('CHANGE:', margin, currentHeight);
      doc.text(`P${receiptData.order.change.toFixed(2)}`, pageWidth - margin, currentHeight, { align: 'right' });
      currentHeight += 8;
      
      // Separator line
      doc.line(margin, currentHeight, pageWidth - margin, currentHeight);
      currentHeight += 6;
      
      // Signature section
      doc.setFont('courier', 'bold');
      doc.setFontSize(8);
      doc.text('Signature:', margin, currentHeight);
      currentHeight += 8;
      
      doc.setLineWidth(0.1);
      doc.line(margin, currentHeight, pageWidth - margin, currentHeight);
      currentHeight += 4;
      doc.setFont('courier', 'normal');
      doc.setFontSize(7);
      doc.text(' Signature', pageWidth / 2, currentHeight, { align: 'center' });
      currentHeight += 8;
      
      // Footer
      doc.setFontSize(7);
      doc.text('Thank you for your purchase!', pageWidth / 2, currentHeight, { align: 'center' });
      currentHeight += 4;
      doc.text('Please come again', pageWidth / 2, currentHeight, { align: 'center' });
      
      // Save the PDF
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