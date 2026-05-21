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
  const [showSalesSuccessModal, setShowSalesSuccessModal] = useState(false);
  const [salesSuccessData, setSalesSuccessData] = useState(null);
  const [user, setUser] = useState(null);
  const [userBusinessId, setUserBusinessId] = useState(null);
  const [businessInfo, setBusinessInfo] = useState(null);

  // Calendar filtering state
  const [rangeMode, setRangeMode] = useState('all');
  const [selectedYear, setSelectedYear] = useState(new Date().getFullYear());
  const [selectedMonth, setSelectedMonth] = useState(formatYYYYMM(new Date()));
  const [selectedWeek, setSelectedWeek] = useState(getCurrentWeekString());
  const [selectedDay, setSelectedDay] = useState(new Date().toISOString().slice(0, 10));
  const [startDate, setStartDate] = useState('');
  const [endDate, setEndDate] = useState('');

  function formatYYYYMM(d) {
    const year = d.getFullYear();
    const month = String(d.getMonth() + 1).padStart(2, '0');
    return `${year}-${month}`;
  }

  function getCurrentWeekString() {
    const today = new Date();
    const startOfWeek = new Date(today);
    startOfWeek.setDate(today.getDate() - today.getDay());
    return startOfWeek.toISOString().slice(0, 10);
  }

  function getWeekRange(weekStartDate) {
    const start = new Date(weekStartDate);
    const end = new Date(start);
    end.setDate(start.getDate() + 6);
    return { start, end };
  }

  function formatWeekDisplay(weekStartDate) {
    const { start, end } = getWeekRange(weekStartDate);
    const startMonth = start.toLocaleString('en-US', { month: 'long' });
    const endMonth = end.toLocaleString('en-US', { month: 'short' });
    const startDay = start.getDate();
    const endDay = end.getDate();
    const year = end.getFullYear();

    if (start.getMonth() === end.getMonth()) {
      return `${startMonth} ${startDay} - ${endDay}, ${year}`;
    } else {
      return `${startMonth} ${startDay} - ${endMonth} ${endDay}, ${year}`;
    }
  }

  // Updated bestsellers calculation to group by product name
  const bestsellers = useMemo(() => {
    if (!orderData.length) return [];

    const summary = {};
    orderData.forEach(item => {
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

  // Fetch user authentication and business information
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
          .select('*, business_id')
          .eq('userid', user.id)
          .single();
        
        if (!mounted) return;
        
        if (profileError) {
          console.error("Error fetching user profile:", profileError);
          return;
        }

        if (!profile.business_id) {
          alert('No business assigned to your account. Please contact administrator.');
          window.location.href = '/Dashboard';
          return;
        }

        setUser(profile);
        setUserBusinessId(profile.business_id);

        // Fetch business details
        const { data: business, error: businessError } = await supabase
          .from('business_role')
          .select('*')
          .eq('businessid', profile.business_id)
          .single();

        if (!mounted) return;

        if (businessError) {
          console.error("Error fetching business info:", businessError);
          return;
        }

        setBusinessInfo(business);
        
      } catch (error) {
        console.error("Authentication error:", error);
        if (mounted) window.location.href = '/';
      }
    };
    
    getUser();
    return () => { mounted = false; };
  }, []);

  // Updated products fetching with business filter
  const fetchProducts = useCallback(async () => {
    if (!userBusinessId) return;

    try {
      // Fetch products for this business
      const { data: businessProducts, error: productsError } = await supabase
        .from('products')
        .select('productid, productname, description, image_url, businessid')
        .eq('businessid', userBusinessId);

      if (productsError) {
        console.error('Error fetching products:', productsError.message);
        return;
      }

      if (!businessProducts || businessProducts.length === 0) {
        setProducts([]);
        return;
      }

      const productIds = businessProducts.map(p => p.productid);

      // Fetch product categories
      const { data: categories, error: categoriesError } = await supabase
        .from('productcategory')
        .select('*')
        .in('productid', productIds)
        .order('productcategoryid');

      if (categoriesError) {
        console.error('Error fetching categories:', categoriesError.message);
        return;
      }

      const transformedProducts = categories.map(cat => {
        const product = businessProducts.find(p => p.productid === cat.productid);
        return {
          productcategoryid: cat.productcategoryid,
          productid: cat.productid,
          productname: product?.productname || 'Unknown Product',
          description: product?.description || '',
          image_url: product?.image_url || '',
          price: cat.price,
          cost: cat.cost,
          color: cat.color,
          agesize: cat.agesize,
          currentstock: cat.currentstock,
          reorderpoint: cat.reorderpoint
        };
      });
      
      setProducts(transformedProducts);
    } catch (error) {
      console.error('Unexpected error fetching products:', error);
    }
  }, [userBusinessId]);

  // Updated order data fetching with business filter
  const fetchOrderData = useCallback(async () => {
    if (!userBusinessId) return;

    try {
      // First, fetch orders for this business only
      const { data: businessOrders, error: ordersError } = await supabase
        .from('orders')
        .select('orderid, ordercode, totalamount, orderstatus, amount_paid, change, orderdate, businessid, userid')
        .eq('businessid', userBusinessId);

      if (ordersError) {
        console.error('Error fetching orders:', ordersError.message);
        setLoading(false);
        return;
      }

      if (!businessOrders || businessOrders.length === 0) {
        console.log('No orders found for this business');
        setOrderData([]);
        setLoading(false);
        return;
      }

      // Get order IDs
      const orderIds = businessOrders.map(order => order.orderid);

      // Fetch order items for these orders
      const { data: orderItemsData, error: orderItemsError } = await supabase
        .from('orderitems')
        .select(`
          productid,
          orderid,
          productcategoryid,
          quantity,
          unitprice,
          subtotal,
          createdat
        `)
        .in('orderid', orderIds);

      if (orderItemsError) {
        console.error('Error fetching order items:', orderItemsError.message);
        setLoading(false);
        return;
      }

      // Get unique product IDs from order items
      const productIds = [...new Set(orderItemsData.map(item => item.productid))];

      // Fetch product details
      const { data: productsData, error: productsDataError } = await supabase
        .from('products')
        .select('productid, productname, image_url, description, businessid')
        .in('productid', productIds)
        .eq('businessid', userBusinessId);

      if (productsDataError) {
        console.error('Error fetching products data:', productsDataError);
      }

      // Create maps for quick lookup
      const ordersMap = new Map(businessOrders.map(order => [order.orderid, order]));
      const productsMap = new Map((productsData || []).map(p => [p.productid, p]));

      // Combine the data
      const transformedData = orderItemsData.map(item => {
        const orderInfo = ordersMap.get(item.orderid);
        const productInfo = productsMap.get(item.productid);
        
        return {
          ...item,
          products: {
            productname: productInfo?.productname || 'Unknown Product',
            image_url: productInfo?.image_url || '',
            description: productInfo?.description || ''
          },
          orders: orderInfo ? {
            totalamount: orderInfo.totalamount,
            orderstatus: orderInfo.orderstatus,
            amount_paid: orderInfo.amount_paid,
            change: orderInfo.change,
            orderdate: orderInfo.orderdate,
            ordercode: orderInfo.ordercode,
            businessid: orderInfo.businessid
          } : {
            totalamount: item.subtotal,
            orderstatus: 'INCOMPLETE',
            amount_paid: null,
            change: null,
            orderdate: item.createdat,
            businessid: userBusinessId
          }
        };
      });

      console.log(`Found ${transformedData.length} order items for business ${userBusinessId}`);
      setOrderData(transformedData);
    } catch (error) {
      console.error('Unexpected error fetching order data:', error);
    } finally {
      setLoading(false);
    }
  }, [userBusinessId]);

  // Initial data fetch
  useEffect(() => {
    if (userBusinessId) {
      fetchOrderData();
      fetchProducts();
    }
  }, [userBusinessId, fetchOrderData, fetchProducts]);

  // Filter data based on calendar selection
  const filteredOrderData = useMemo(() => {
    return orderData.filter(item => {
      const orderDate = item.orders?.orderdate || item.createdat;
      if (!orderDate) return false;

      const date = new Date(orderDate);
      if (isNaN(date.getTime())) return false;

      const year = date.getFullYear();
      const month = date.getMonth() + 1;
      const day = date.getDate();

      switch (rangeMode) {
        case 'all':
          return true;
        
        case 'year':
          return year === selectedYear;
        
        case 'month': {
          const [y, m] = selectedMonth.split('-').map(Number);
          return year === y && month === m;
        }
        
        case 'week': {
          const { start, end } = getWeekRange(selectedWeek);
          const itemDate = new Date(year, month - 1, day);
          const startDate = new Date(start.getFullYear(), start.getMonth(), start.getDate());
          const endDate = new Date(end.getFullYear(), end.getMonth(), end.getDate());
          return itemDate >= startDate && itemDate <= endDate;
        }
        
        case 'day': {
          const itemDateStr = `${year}-${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}`;
          return itemDateStr === selectedDay;
        }
        
        case 'range': {
          if (!startDate || !endDate) return false;
          const itemDateStr = `${year}-${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}`;
          return itemDateStr >= startDate && itemDateStr <= endDate;
        }
        
        default:
          return true;
      }
    });
  }, [orderData, rangeMode, selectedYear, selectedMonth, selectedWeek, selectedDay, startDate, endDate]);

  const handleSalesSuccessModalClose = useCallback(() => {
    setShowSalesSuccessModal(false);
    setSalesSuccessData(null);
  }, []);

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
        .eq('orderid', updateOrderData.orderid)
        .eq('businessid', userBusinessId); // Ensure user can only update their business orders

      if (orderUpdateError) {
        console.error('Database update error:', orderUpdateError);
        throw new Error(`Failed to update order: ${orderUpdateError.message}`);
      }

      await fetchOrderData();
    } catch (error) {
      console.error('Error updating order:', error);
      throw error;
    }
  }, [fetchOrderData, userBusinessId]);

  const handleInvoiceSelect = useCallback(async (selectedItem) => {
    try {
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
            productid
          `)
          .eq('orderid', selectedItem.orderid);

        if (error) {
          console.error('Error fetching order items:', error);
          alert('Error loading invoice details');
          return;
        }

        // Fetch order details
        const { data: orderDetails, error: orderError } = await supabase
          .from('orders')
          .select('*')
          .eq('orderid', selectedItem.orderid)
          .eq('businessid', userBusinessId)
          .single();

        if (orderError) {
          console.error('Error fetching order details:', orderError);
          alert('Error loading order details');
          return;
        }

        // Fetch product details for the items
        const productIds = [...new Set(orderItems.map(item => item.productid))];
        const { data: products, error: productsError } = await supabase
          .from('products')
          .select('productid, productname, image_url, description')
          .in('productid', productIds)
          .eq('businessid', userBusinessId);

        if (productsError) {
          console.error('Error fetching products:', productsError);
        }

        const productsMap = new Map((products || []).map(p => [p.productid, p]));

        const transformedOrderItems = orderItems.map(item => {
          const product = productsMap.get(item.productid);
          return {
            ...item,
            products: {
              productname: product?.productname || 'Unknown Product',
              image_url: product?.image_url || ''
            },
            orders: {
              totalamount: orderDetails.totalamount,
              orderstatus: orderDetails.orderstatus,
              amount_paid: orderDetails.amount_paid,
              change: orderDetails.change,
              orderdate: orderDetails.orderdate
            }
          };
        });

        setSelectedInvoice({
          ...selectedItem,
          orderItems: transformedOrderItems,
          totalOrderAmount: orderDetails.totalamount,
          orderStatus: orderDetails.orderstatus,
          amount_paid: orderDetails.amount_paid,
          change: orderDetails.change,
          orderdate: orderDetails.orderdate,
          ordercode: orderDetails.ordercode,
          orders: {
            totalamount: orderDetails.totalamount,
            orderstatus: orderDetails.orderstatus,
            amount_paid: orderDetails.amount_paid,
            change: orderDetails.change,
            orderdate: orderDetails.orderdate,
            ordercode: orderDetails.ordercode
          }
        });
      });
    } catch (error) {
      console.error('Error loading invoice:', error);
      alert('Error loading invoice details');
    }
  }, [userBusinessId]);

  const getFilterLabel = () => {
    switch (rangeMode) {
      case 'year':
        return `Year: ${selectedYear}`;
      case 'month':
        const [y, m] = selectedMonth.split('-');
        return `${new Date(y, m - 1).toLocaleString('default', { month: 'long' })} ${y}`;
      case 'week':
        return formatWeekDisplay(selectedWeek);
      case 'day':
        return new Date(selectedDay).toLocaleDateString('en-US', { 
          year: 'numeric', 
          month: 'long', 
          day: 'numeric' 
        });
      case 'range':
        if (startDate && endDate) {
          const start = new Date(startDate).toLocaleDateString('en-US', { 
            year: 'numeric', 
            month: 'long', 
            day: 'numeric' 
          });
          const end = new Date(endDate).toLocaleDateString('en-US', { 
            year: 'numeric', 
            month: 'long', 
            day: 'numeric' 
          });
          return `${start} - ${end}`;
        }
        return 'Select Date Range';
      default:
        return 'All Time';
    }
  };

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
              <li onClick={() => navigate("/TablePage")}>Sales</li>
              <li onClick={() => navigate("/expenses")}>Expenses</li>
              <li onClick={() => navigate("/assistant")}>AI Assistant</li>
              {/* COMMENTED OUT: Business Maturity link removed (feature did not meet team/advisor standards) */}
              {/* <li onClick={() => navigate("/maturity-report")}>Business Maturity</li> */}
            </ul>
            <p className="nav-header">RELATED</p>
            <ul>
              <li onClick={() => navigate("/supplier")}>Supplier</li>
              <li onClick={() => navigate("/pos")}>Point of Sales</li>
              <li onClick={() => navigate("/online-orders")}>Online Orders</li>
              <li onClick={() => navigate("/PlannedPaymentsPage")}>Planned Payment</li>
            </ul>
          </div>
        </aside>

        <div className="main-content">
          {loading ? (
            <div className="loading-states">Loading sales data...</div>
          ) : (
            <>
              <div className="table-flex-wrapper">
                {/* Row 1, Column 1 - Sales Summary - UPDATED: Added userBusinessId prop */}
                <div className="net-income">
                  <SalesSummary 
                    orderData={filteredOrderData}
                    rangeMode={rangeMode}
                    selectedYear={selectedYear}
                    selectedMonth={selectedMonth}
                    selectedWeek={selectedWeek}
                    selectedDay={selectedDay}
                    startDate={startDate}
                    endDate={endDate}
                    userBusinessId={userBusinessId}
                  />
                </div>

                {/* Row 1, Column 2 - Calendar Filter Controls */}
                <div className="calendar-filter-container">
                  <h3>Filter by Date</h3>
                  <div className="filter-controls-wrapper">
                    <div className="filter-field">
                      <label>Filter Mode</label>
                      <select 
                        value={rangeMode}
                        onChange={(e) => setRangeMode(e.target.value)}
                      >
                        <option value="all">All Time</option>
                        <option value="year">By Year</option>
                        <option value="month">By Month</option>
                        <option value="week">By Week</option>
                        <option value="day">By Day</option>
                        <option value="range">Date Range</option>
                      </select>
                    </div>

                    {rangeMode === 'year' && (
                      <div className="filter-field">
                        <label>Select Year</label>
                        <input
                          type="number"
                          value={selectedYear}
                          onChange={(e) => setSelectedYear(Number(e.target.value))}
                          min="2000"
                          max="2100"
                        />
                      </div>
                    )}

                    {rangeMode === 'month' && (
                      <div className="filter-field">
                        <label>Select Month</label>
                        <input
                          type="month"
                          value={selectedMonth}
                          onChange={(e) => setSelectedMonth(e.target.value)}
                        />
                      </div>
                    )}

                    {rangeMode === 'week' && (
                      <div className="filter-field">
                        <label>Select Week (Starting Sunday)</label>
                        <input
                          type="date"
                          value={selectedWeek}
                          onChange={(e) => {
                            const selected = new Date(e.target.value);
                            const startOfWeek = new Date(selected);
                            startOfWeek.setDate(selected.getDate() - selected.getDay());
                            setSelectedWeek(startOfWeek.toISOString().slice(0, 10));
                          }}
                        />
                      </div>
                    )}

                    {rangeMode === 'day' && (
                      <div className="filter-field">
                        <label>Select Date</label>
                        <input
                          type="date"
                          value={selectedDay}
                          onChange={(e) => setSelectedDay(e.target.value)}
                        />
                      </div>
                    )}

                    {rangeMode === 'range' && (
                      <>
                        <div className="filter-field">
                          <label>Start Date</label>
                          <input
                            type="date"
                            value={startDate}
                            onChange={(e) => setStartDate(e.target.value)}
                          />
                        </div>
                        <div className="filter-field">
                          <label>End Date</label>
                          <input
                            type="date"
                            value={endDate}
                            onChange={(e) => setEndDate(e.target.value)}
                            min={startDate}
                          />
                        </div>
                      </>
                    )}

                    <div className="filter-label-display">
                      📅 {getFilterLabel()}
                    </div>
                  </div>
                </div>

                {/* Row 2, Column 1 - Order Sales */}
                <OrderSales 
                  orderData={filteredOrderData}
                  onInvoiceSelect={handleInvoiceSelect}
                  businessName={businessInfo?.businessname}
                />

                {/* Row 2, Column 2 - Bestseller and Peak Hours */}
                <div className="right-column-wrapper">
                  <Bestseller 
                    bestsellers={bestsellers} 
                    orderData={filteredOrderData} 
                  />
                  <div className="bottom-analytics-wrapper">
                    <PeakHours orderData={filteredOrderData} />
                  </div>
                </div>
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