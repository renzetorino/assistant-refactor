import React, { useEffect, useState, useCallback, useMemo } from 'react';
import { useNavigate } from "react-router-dom";
import { supabase } from './supabase';
import OrderSales from './components/OrderSales';
import Bestseller from './components/Bestseller';
import InvoiceModal from './components/InvoiceModal';
import SalesSuccessModal from './components/SalesSuccessModal';
import PeakHours from './components/PeakHours';
import SalesSummary from './components/SalesSummary';
import './stylecss/TablePage.css';
import './stylecss/Sales/OrderSales.css';
import './stylecss/Sales/InvoiceModal.css';
import './stylecss/Sales/Bestseller.css';
import './stylecss/Sales/PeakHours.css';
import './stylecss/Sales/SalesSummary.css';

const TablePage = () => {
  const navigate = useNavigate();
  const [orderData, setOrderData] = useState([]);
  const [_products, setProducts] = useState([]);
  const [loading, setLoading] = useState(true);
  const [selectedInvoice, setSelectedInvoice] = useState(null);
  const [statsFilter, setStatsFilter] = useState('all');
  const [showSalesSuccessModal, setShowSalesSuccessModal] = useState(false);
  const [salesSuccessData, setSalesSuccessData] = useState(null);
  const [_user, setUser] = useState(null);

  // Updated bestsellers calculation to group by product name instead of productcategoryid
  const bestsellers = useMemo(() => {
    if (!orderData.length) return [];

    const summary = {};
    orderData.forEach(item => {
      // Use product name as the unique identifier instead of productcategoryid
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

  // Optimize user authentication check
  useEffect(() => {
    let mounted = true;

    const getUser = async () => {
      try {
        const { data: { user }, error } = await supabase.auth.getUser();
        if (!mounted) return;

        if (error || !user) {
          window.location.href = '/';
          return;
        }
        
        const { data: profile, error: profileError } = await supabase
          .from('systemuser')
          .select('*')
          .eq('userid', user.id)
          .single();
        
        if (!mounted) return;
        
        if (profileError) {
          console.error("Error fetching user profile:", profileError);
          return;
        }
        
        setUser(profile);
      } catch (error) {
        console.error("Authentication error:", error);
        if (mounted) window.location.href = '/';
      }
    };
    
    getUser();
    return () => { mounted = false; };
  }, []);

  // Updated products fetching to use productcategory table
  const fetchProducts = useCallback(async () => {
    try {
      const { data, error } = await supabase
        .from('productcategory')
        .select(`
          productcategoryid,
          productid,
          price,
          cost,
          color,
          agesize,
          currentstock,
          reorderpoint,
          products (
            productname,
            description,
            image_url
          )
        `)
        .order('productcategoryid');

      if (error) {
        console.error('Error fetching products:', error.message);
        return;
      }
      
      // Transform data to match expected format
      const transformedProducts = data?.map(item => ({
        productcategoryid: item.productcategoryid,
        productid: item.productid,
        productname: item.products?.productname || 'Unknown Product',
        description: item.products?.description || '',
        image_url: item.products?.image_url || '',
        price: item.price,
        cost: item.cost,
        color: item.color,
        agesize: item.agesize,
        currentstock: item.currentstock,
        reorderpoint: item.reorderpoint
      })) || [];
      
      setProducts(transformedProducts);
    } catch (error) {
      console.error('Unexpected error fetching products:', error);
    }
  }, []);

  // Updated order data fetching to include orderdate from orders table
  const fetchOrderData = useCallback(async () => {
    try {
      const { data, error } = await supabase
        .from('orderitems')
        .select(`
          productid,
          orderid,
          productcategoryid,
          quantity,
          unitprice,
          subtotal,
          createdat,
          productcategory (
            productid,
            price,
            cost,
            color,
            agesize,
            currentstock,
            products (
              productname,
              image_url,
              description
            )
          ),
          orders (
            totalamount,
            orderstatus,
            amount_paid,
            change,
            orderdate
          )
        `);

      if (error) {
        console.error('Error fetching order data:', error.message);
        return;
      }

      // Transform data to match expected format
      const transformedData = data?.map(item => ({
        ...item,
        // Create a products object for backward compatibility
        products: {
          productname: item.productcategory?.products?.productname || 'Unknown Product',
          image_url: item.productcategory?.products?.image_url || '',
          description: item.productcategory?.products?.description || ''
        }
      })) || [];

      setOrderData(transformedData);
    } catch (error) {
      console.error('Unexpected error fetching order data:', error);
    } finally {
      setLoading(false);
    }
  }, []);

  // Initial data fetch
  useEffect(() => {
    fetchOrderData();
    fetchProducts();
  }, [fetchOrderData, fetchProducts]);

  // Debounce stats filter changes
  const handleStatsFilter = useCallback((value) => {
    // Use requestAnimationFrame to defer state update
    requestAnimationFrame(() => {
      setStatsFilter(value);
    });
  }, []);

  // Add callback for sales success modal
  const handleSalesSuccessModalClose = useCallback(() => {
    setShowSalesSuccessModal(false);
    setSalesSuccessData(null);
  }, []);

  // Optimize update order with async processing
  const handleUpdateOrder = useCallback(async (updateOrderData) => {
    try {
      const normalizedStatus = updateOrderData.orderStatus.toUpperCase();
      
      const { error: orderUpdateError } = await supabase
        .from('orders')
        .update({
          amount_paid: updateOrderData.amountPaid,
          change: updateOrderData.change,
          orderstatus: normalizedStatus
        })
        .eq('orderid', updateOrderData.orderid);

      if (orderUpdateError) {
        console.error('Database update error:', orderUpdateError);
        throw new Error(`Failed to update order: ${orderUpdateError.message}`);
      }

      // Fetch data asynchronously
      await fetchOrderData();
    } catch (error) {
      console.error('Error updating order:', error);
      throw error;
    }
  }, [fetchOrderData]);

  // Updated invoice selection to handle new structure with variant data
  const handleInvoiceSelect = useCallback(async (selectedItem) => {
    try {
      // Use requestAnimationFrame to defer heavy operations
      requestAnimationFrame(async () => {
        const { data: orderItems, error } = await supabase
          .from('orderitems')
          .select(`
            orderid,
            productcategoryid,
            quantity,
            unitprice,
            subtotal,
            createdat,
            productcategory (
              productid,
              price,
              color,
              agesize,
              products (
                productname,
                image_url,
                description
              )
            ),
            orders (
              totalamount,
              orderstatus,
              amount_paid,
              change,
              orderdate
            )
          `)
          .eq('orderid', selectedItem.orderid);

        if (error) {
          console.error('Error fetching order items:', error);
          alert('Error loading invoice details');
          return;
        }

        // Transform data for backward compatibility
        const transformedOrderItems = orderItems?.map(item => ({
          ...item,
          products: {
            productname: item.productcategory?.products?.productname || 'Unknown Product',
            image_url: item.productcategory?.products?.image_url || ''
          }
        })) || [];

        setSelectedInvoice({
          ...selectedItem,
          orderItems: transformedOrderItems,
          totalOrderAmount: transformedOrderItems[0]?.orders?.totalamount || 0,
          orderStatus: transformedOrderItems[0]?.orders?.orderstatus || 'INCOMPLETE',
          amount_paid: transformedOrderItems[0]?.orders?.amount_paid,
          change: transformedOrderItems[0]?.orders?.change,
          orderdate: transformedOrderItems[0]?.orders?.orderdate,
          orders: {
            totalamount: transformedOrderItems[0]?.orders?.totalamount,
            orderstatus: transformedOrderItems[0]?.orders?.orderstatus,
            amount_paid: transformedOrderItems[0]?.orders?.amount_paid,
            change: transformedOrderItems[0]?.orders?.change,
            orderdate: transformedOrderItems[0]?.orders?.orderdate
          }
        });
      });
    } catch (error) {
      console.error('Error loading invoice:', error);
      alert('Error loading invoice details');
    }
  }, []);

  return (
    <div className="sales-page">
      <header className="header-bar">
        <h1 className="header-title">BuiswAIz</h1>
      </header>
      
      <div className="main-section">
        <aside className="sidebar">
          <div className="nav-section">
            <p className="nav-header">GENERAL</p>
            <ul>
              <li onClick={() => navigate("/Dashboard")}>Dashboard</li>
              <li onClick={() => navigate("/inventory")}>Inventory</li>
              <li className="active">Sales</li>
              <li onClick={() => navigate("/expenses")}>Expenses</li>
              <li onClick={() => navigate("/assistant")}>AI Assistant</li>
            </ul>
            <p className="nav-header">RELATED</p>
            <ul>
              <li onClick={() => navigate("/supplier")}>Supplier</li>
              <li onClick={() => navigate("/pos")}>Point of Sales</li>
              <li onClick={() => navigate("/PlannedPaymentsPage")}>Planned Payment</li>
            </ul>
          </div>
        </aside>

        <div className="main-content">
          {loading ? (
            <div className="loading-states">
            </div>
          ) : (
            <>
              <div className="table-flex-wrapper">
                {/* Row 1 - Net Income (Full Width) - Now includes Total Customers and Sales Trend */}
                <div className="net-income">
                  <SalesSummary 
                    orderData={orderData}
                    statsFilter={statsFilter}
                  />
                </div>

                {/* Row 2, Column 1 - Order Sales */}
                <OrderSales 
                  orderData={orderData}
                  onInvoiceSelect={handleInvoiceSelect}
                />

                {/* Row 2, Column 2 - Bestseller */}
                <div className="right-column-wrapper">
                  <Bestseller bestsellers={bestsellers} orderData={orderData} />
                  <div className="bottom-analytics-wrapper">
                  <PeakHours orderData={orderData} />
                  </div>
                </div>

                {/* Row 3 - Peak Hours (Full Width) */}
              </div>
            </>
          )}
        </div>
      </div>

      {selectedInvoice && (
        <InvoiceModal 
          invoice={selectedInvoice}
          onClose={() => setSelectedInvoice(null)}
          onUpdateOrder={handleUpdateOrder}
        />
      )}

      <SalesSuccessModal 
        isOpen={showSalesSuccessModal}
        onClose={handleSalesSuccessModalClose}
        orderData={salesSuccessData}
      />
    </div>
  );
};

export default TablePage;