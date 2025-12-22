import React, { useState, useEffect, useRef } from 'react';
import { supabase } from '../supabase';
import '../stylecss/PointOfSales/SalesReceipt.css';

const SalesReceipt = ({ orderId, orderCode: propOrderCode, onClose }) => {
  const [receiptData, setReceiptData] = useState(null);
  const [loading, setLoading] = useState(true);
  const [businessData, setBusinessData] = useState(null);
  const [orderCode, setOrderCode] = useState(propOrderCode || null);
  const receiptRef = useRef(null);

  useEffect(() => {
    if (orderId) {
      fetchReceiptData();
    }
  }, [orderId]);

  const fetchReceiptData = async () => {
    try {
      // Fetch order details with business information
      const { data: orderData, error: orderError } = await supabase
        .from('orders')
        .select(`
          *,
          business_role (
            businessid,
            businessname,
            businesscode,
            businessAddress
          )
        `)
        .eq('orderid', orderId)
        .single();

      if (orderError) throw orderError;

      // Set order code from database if not provided as prop
      if (!propOrderCode && orderData.ordercode) {
        setOrderCode(orderData.ordercode);
      }

      // Fetch order items with product details
      const { data: itemsData, error: itemsError } = await supabase
        .from('orderitems')
        .select(`
          orderitemid,
          quantity,
          unitprice,
          subtotal,
          productid,
          productcategoryid
        `)
        .eq('orderid', orderId);

      if (itemsError) throw itemsError;

      // Fetch product details separately
      const productIds = [...new Set(itemsData.map(item => item.productid))];
      const { data: productsData } = await supabase
        .from('products')
        .select('productid, productname')
        .in('productid', productIds);

      // Fetch product category details
      const categoryIds = itemsData.map(item => item.productcategoryid);
      const { data: categoriesData } = await supabase
        .from('productcategory')
        .select('productcategoryid, color, agesize')
        .in('productcategoryid', categoryIds);

      // Create maps for quick lookup
      const productsMap = new Map((productsData || []).map(p => [p.productid, p]));
      const categoriesMap = new Map((categoriesData || []).map(c => [c.productcategoryid, c]));

      // Combine data
      const enrichedItems = itemsData.map(item => ({
        ...item,
        products: {
          productname: productsMap.get(item.productid)?.productname || 'Unknown Product'
        },
        productcategory: categoriesMap.get(item.productcategoryid) || {}
      }));

      setReceiptData({
        order: orderData,
        items: enrichedItems
      });
      setBusinessData(orderData.business_role);
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
      const pageWidth = 80;
      const margin = 5;
      const contentWidth = pageWidth - (margin * 2);
      let currentHeight = 10;
      
      let estimatedHeight = 100;
      estimatedHeight += receiptData.items.length * 20;
      const finalHeight = Math.max(Math.min(estimatedHeight, 300), 200);
      
      const doc = new jsPDF({
        orientation: 'portrait',
        unit: 'mm',
        format: [pageWidth, finalHeight]
      });

      const { date, time } = formatDateTime(receiptData.order.orderdate);
      
      doc.setFont('courier');
      
      // Header - Platform Name
      doc.setFontSize(14);
      doc.setFont('courier', 'bold');
      doc.text('BuiswAIz', pageWidth / 2, currentHeight, { align: 'center' });
      currentHeight += 7;
      
      // Receipt Number (Order Code)
      doc.setFontSize(10);
      doc.setFont('courier', 'normal');
      const displayCode = orderCode || `ORDER-${orderId}`;
      doc.text(`Receipt #: ${displayCode}`, pageWidth / 2, currentHeight, { align: 'center' });
      currentHeight += 8;
      
      // Business Information
      doc.setFontSize(8);
      doc.setFont('courier', 'bold');
      doc.text('Store Name:', margin, currentHeight);
      doc.setFont('courier', 'normal');
      const storeName = businessData?.businessname || 'N/A';
      doc.text(storeName, margin + 20, currentHeight);
      currentHeight += 5;
      
      // Address
      if (businessData?.businessAddress) {
        doc.setFont('courier', 'bold');
        doc.text('Address:', margin, currentHeight);
        doc.setFont('courier', 'normal');
        currentHeight += 4;
        const addressLines = doc.splitTextToSize(businessData.businessAddress, contentWidth);
        addressLines.forEach(line => {
          doc.text(line, margin, currentHeight);
          currentHeight += 4;
        });
      }
      currentHeight += 1;
      
      doc.text(`Date: ${date}`, margin, currentHeight);
      currentHeight += 4;
      doc.text(`Time: ${time}`, margin, currentHeight);
      currentHeight += 7;

      // Separator
      doc.setLineWidth(0.3);
      doc.line(margin, currentHeight, pageWidth - margin, currentHeight);
      currentHeight += 5;

      // Notice
      doc.setFont('courier', 'normal');
      doc.setFontSize(7);
      doc.text('This is not the official receipt', pageWidth / 2, currentHeight, { align: 'center' });
      currentHeight += 5;
      
      doc.line(margin, currentHeight, pageWidth - margin, currentHeight);
      currentHeight += 5;
      
      // Items Header
      doc.setFont('courier', 'bold');
      doc.setFontSize(8);
      doc.text('Item', margin, currentHeight);
      doc.text('Qty', pageWidth - margin - 25, currentHeight, { align: 'left' });
      doc.text('Price', pageWidth - margin, currentHeight, { align: 'right' });
      currentHeight += 4;
      
      doc.setLineWidth(0.1);
      doc.line(margin, currentHeight, pageWidth - margin, currentHeight);
      currentHeight += 4;
      
      // Items
      doc.setFont('courier', 'normal');
      doc.setFontSize(8);
      
      receiptData.items.forEach((item, index) => {
        const productName = item.products?.productname || 'Unknown Product';
        const maxWidth = contentWidth - 5;
        const lines = doc.splitTextToSize(productName, maxWidth);
        
        lines.forEach((line) => {
          doc.text(line, margin, currentHeight);
          currentHeight += 4;
        });
        
        if (item.productcategory?.agesize || item.productcategory?.color) {
          const variant = [item.productcategory?.agesize, item.productcategory?.color]
            .filter(Boolean)
            .join(' - ');
          doc.setFontSize(7);
          doc.text(`(${variant})`, margin + 2, currentHeight);
          currentHeight += 4;
          doc.setFontSize(8);
        }
        
        const qtyY = currentHeight;
        doc.text(`${item.quantity}x`, pageWidth - margin - 25, qtyY);
        doc.text(`P${item.unitprice.toFixed(2)}`, pageWidth - margin, qtyY, { align: 'right' });
        currentHeight += 4;
        
        doc.setFont('courier', 'bold');
        doc.text(`P${item.subtotal.toFixed(2)}`, pageWidth - margin, currentHeight, { align: 'right' });
        doc.setFont('courier', 'normal');
        currentHeight += 5;
        
        if (index < receiptData.items.length - 1) {
          currentHeight += 2;
        }
      });
      
      currentHeight += 3;
      
      // Separator
      doc.setLineWidth(0.3);
      doc.line(margin, currentHeight, pageWidth - margin, currentHeight);
      currentHeight += 5;
      
      // Totals
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
      
      doc.line(margin, currentHeight, pageWidth - margin, currentHeight);
      currentHeight += 6;
      
      // Signature
      doc.setFont('courier', 'bold');
      doc.setFontSize(8);
      doc.text('Signature:', margin, currentHeight);
      currentHeight += 8;
      
      doc.setLineWidth(0.1);
      doc.line(margin, currentHeight, pageWidth - margin, currentHeight);
      currentHeight += 4;
      doc.setFont('courier', 'normal');
      doc.setFontSize(7);
      doc.text('[Signature]', pageWidth / 2, currentHeight, { align: 'center' });
      currentHeight += 8;
      
      // Footer
      doc.setFontSize(7);
      doc.text('Thank you for your purchase!', pageWidth / 2, currentHeight, { align: 'center' });
      currentHeight += 4;
      doc.text('Please come again', pageWidth / 2, currentHeight, { align: 'center' });
      
      const filename = orderCode 
        ? `Receipt_${orderCode}.pdf`
        : `Receipt_Order_${orderId}.pdf`;
      doc.save(filename);
      
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
  const displayCode = orderCode || `ORDER-${orderId}`;

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
                <div className="order-id" style={{ fontFamily: 'monospace', fontSize: '16px', fontWeight: 'bold' }}>
                  Receipt #: {displayCode}
                </div>
              </div>

              <div className="info-section">
                <div><span className="info-label">Store Name:</span> {businessData?.businessname || 'N/A'}</div>
                {businessData?.businessAddress && (
                  <div><span className="info-label">Address:</span> {businessData.businessAddress}</div>
                )}
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