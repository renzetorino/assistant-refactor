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
  const [_user, setUser] = useState(null);

  // Calendar filtering state
  const [rangeMode, setRangeMode] = useState('all'); // 'all' | 'year' | 'month' | 'week' | 'day' | 'range'
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

  // Updated products fetching
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

  // Updated order data fetching - FIXED to fetch ALL orderitems
  const fetchOrderData = useCallback(async () => {
    try {
      // First, fetch all orderitems with their product details
      const { data: orderItemsData, error: orderItemsError } = await supabase
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
          )
        `);

      if (orderItemsError) {
        console.error('Error fetching order items:', orderItemsError.message);
        return;
      }

      // Get unique order IDs
      const orderIds = [...new Set(orderItemsData?.map(item => item.orderid) || [])];

      // Fetch corresponding orders data
      const { data: ordersData, error: ordersError } = await supabase
        .from('orders')
        .select('orderid, totalamount, orderstatus, amount_paid, change, orderdate')
        .in('orderid', orderIds);

      if (ordersError) {
        console.error('Error fetching orders:', ordersError.message);
      }

      // Create a map of orders for quick lookup
      const ordersMap = new Map();
      ordersData?.forEach(order => {
        ordersMap.set(order.orderid, order);
      });

      // Combine the data
      const transformedData = orderItemsData?.map(item => {
        const orderInfo = ordersMap.get(item.orderid);
        
        return {
          ...item,
          products: {
            productname: item.productcategory?.products?.productname || 'Unknown Product',
            image_url: item.productcategory?.products?.image_url || '',
            description: item.productcategory?.products?.description || ''
          },
          orders: orderInfo ? {
            totalamount: orderInfo.totalamount,
            orderstatus: orderInfo.orderstatus,
            amount_paid: orderInfo.amount_paid,
            change: orderInfo.change,
            orderdate: orderInfo.orderdate
          } : {
            // Fallback if order data is missing
            totalamount: item.subtotal,
            orderstatus: 'INCOMPLETE',
            amount_paid: null,
            change: null,
            orderdate: item.createdat // Use createdat as fallback
          }
        };
      }) || [];

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

  // Filter data based on calendar selection - FIXED FOR PHILIPPINES TIMEZONE
  const filteredOrderData = useMemo(() => {
    return orderData.filter(item => {
      // Use orderdate if available, otherwise fall back to createdat
      const orderDate = item.orders?.orderdate || item.createdat;
      if (!orderDate) return false;

      // Parse the date string and create a date in local timezone
      const date = new Date(orderDate);
      if (isNaN(date.getTime())) return false;

      // Extract year, month, day in LOCAL timezone (Philippines)
      const year = date.getFullYear();
      const month = date.getMonth() + 1; // 0-indexed, so add 1
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
          // Create date objects using local date components only
          const itemDate = new Date(year, month - 1, day);
          const startDate = new Date(start.getFullYear(), start.getMonth(), start.getDate());
          const endDate = new Date(end.getFullYear(), end.getMonth(), end.getDate());
          return itemDate >= startDate && itemDate <= endDate;
        }
        
        case 'day': {
          // Compare the date string directly (YYYY-MM-DD format)
          const itemDateStr = `${year}-${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}`;
          return itemDateStr === selectedDay;
        }
        
        case 'range': {
          // Date range filtering
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
        .eq('orderid', updateOrderData.orderid);

      if (orderUpdateError) {
        console.error('Database update error:', orderUpdateError);
        throw new Error(`Failed to update order: ${orderUpdateError.message}`);
      }

      await fetchOrderData();
    } catch (error) {
      console.error('Error updating order:', error);
      throw error;
    }
  }, [fetchOrderData]);

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
                {/* Row 1, Column 1 - Sales Summary */}
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