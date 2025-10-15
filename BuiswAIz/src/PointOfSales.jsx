import React, { useState, useEffect, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { supabase } from './supabase';
import ItemsPanel from './PointOfSales/ItemsPanel';
import SellingPanel from './PointOfSales/SellingPanel';
import SalesSuccessModal from './components/SalesSuccessModal';
import "./stylecss/PointOfSales.css";

const PointOfSales = () => {
  const navigate = useNavigate();
  
  const [products, setProducts] = useState([]);
  const [cart, setCart] = useState([]);
  const [amountPaid, setAmountPaid] = useState('');
  const [searchTerm, setSearchTerm] = useState('');
  const [selectedCategory, setSelectedCategory] = useState('All');
  const [loading, setLoading] = useState(true);
  // eslint-disable-next-line no-unused-vars
  const [user, setUser] = useState(null);
  const [categories, setCategories] = useState(['All']);
  const [showSuccessModal, setShowSuccessModal] = useState(false);
  const [orderData, setOrderData] = useState({});

  // Fetch user authentication 
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

  // Fetch products from Supabase
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
        .gt('currentstock', 0)
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
        reorderpoint: item.reorderpoint,
        // Create category label from agesize only
        categoryLabel: item.agesize || 'Uncategorized',
        displayName: [
          item.products?.productname,
          item.color && `(${item.color})`,
          item.agesize && `[${item.agesize}]`
        ].filter(Boolean).join(' ')
      })) || [];
      
      setProducts(transformedProducts);
      
      // Extract unique categories from agesize only
      const uniqueCategories = ['All', ...new Set(
        transformedProducts
          .map(p => p.agesize)
          .filter(Boolean)
      )].sort();
      setCategories(uniqueCategories);
      
    } catch (error) {
      console.error('Unexpected error fetching products:', error);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchProducts();
  }, [fetchProducts]);

  // Add to cart
  const addToCart = (product) => {
    const existingItem = cart.find(item => item.productcategoryid === product.productcategoryid);
    
    if (existingItem) {
      if (existingItem.quantity + 1 > product.currentstock) {
        alert(`Cannot add more. Only ${product.currentstock} in stock.`);
        return;
      }
      
      setCart(cart.map(item =>
        item.productcategoryid === product.productcategoryid
          ? { ...item, quantity: item.quantity + 1 }
          : item
      ));
    } else {
      setCart([...cart, { ...product, quantity: 1 }]);
    }
  };

  // Remove from cart
  const removeFromCart = (productCategoryId) => {
    setCart(cart.filter(item => item.productcategoryid !== productCategoryId));
  };

  // Update quantity
  const updateQuantity = (productCategoryId, newQuantity) => {
    if (newQuantity <= 0) {
      removeFromCart(productCategoryId);
      return;
    }
    
    const product = products.find(p => p.productcategoryid === productCategoryId);
    if (product && newQuantity > product.currentstock) {
      alert(`Cannot add more. Only ${product.currentstock} in stock.`);
      return;
    }
    
    setCart(cart.map(item =>
      item.productcategoryid === productCategoryId 
        ? { ...item, quantity: newQuantity } 
        : item
    ));
  };

  // Generate unique order ID
  const generateUniqueOrderId = async () => {
    try {
      const { data, error } = await supabase
        .from('orders')
        .select('orderid')
        .order('orderid', { ascending: false })
        .limit(1);
      
      if (error) throw new Error(`Database error: ${error.message}`);
      
      if (!data || data.length === 0) return 1;
      
      const highestOrderId = parseInt(data[0].orderid, 10);
      return isNaN(highestOrderId) ? 1 : highestOrderId + 1;
    } catch (error) {
      console.error('Error generating order ID:', error);
      throw error;
    }
  };

  // Complete transaction - Updated to accept datetime parameter
  const completeTransaction = async (dateTimeData) => {
    if (cart.length === 0) {
      alert('Cart is empty!');
      return;
    }

    if (!amountPaid || parseFloat(amountPaid) <= 0) {
      alert('Please enter the amount paid by customer.');
      return;
    }

    const { orderDate, orderTime } = dateTimeData;

    if (!orderDate || !orderTime) {
      alert('Please select date and time for the transaction.');
      return;
    }

    const subtotal = cart.reduce((sum, item) => sum + (item.price * item.quantity), 0);
    const total = subtotal;

    // Determine order status based on payment
    const paidAmount = parseFloat(amountPaid);
    const orderStatus = paidAmount >= total ? 'COMPLETE' : 'INCOMPLETE';
    const change = paidAmount >= total ? (paidAmount - total) : 0;

    try {
      const uniqueOrderId = await generateUniqueOrderId();
      
      // Use the provided date and time
      const orderDateTime = `${orderDate} ${orderTime}:00`;

      const orderRecord = {
        orderid: uniqueOrderId,
        totalamount: total,
        orderdate: orderDateTime,
        orderstatus: orderStatus,
        amount_paid: paidAmount,
        change: change
      };
      
      const { error: orderError } = await supabase
        .from('orders')
        .insert([orderRecord]);

      if (orderError) throw new Error(`Order creation failed: ${orderError.message}`);

      const orderItems = cart.map(item => ({
        orderid: uniqueOrderId,
        productid: item.productid,
        productcategoryid: item.productcategoryid,
        quantity: item.quantity,
        unitprice: item.price,
        subtotal: item.price * item.quantity,
        createdat: orderDateTime
      }));

      const { error: itemsError } = await supabase
        .from('orderitems')
        .insert(orderItems);

      if (itemsError) throw new Error(`Order items creation failed: ${itemsError.message}`);

      // Update inventory stock
      for (const item of cart) {
        const newStock = item.currentstock - item.quantity;
        
        const { error: stockError } = await supabase
          .from('productcategory')
          .update({
            currentstock: newStock,
            updatedstock: orderDateTime
          })
          .eq('productcategoryid', item.productcategoryid);

        if (stockError) throw new Error(`Stock update failed: ${stockError.message}`);
      }
      
      // Set order data for modal
      setOrderData({
        orderId: uniqueOrderId,
        totalAmount: total,
        amountPaid: paidAmount,
        change: change,
        status: orderStatus,
        itemCount: cart.length
      });
      
      // Clear cart and show success modal
      setCart([]);
      setAmountPaid('');
      setShowSuccessModal(true);
      await fetchProducts();
      
    } catch (error) {
      console.error('Transaction error:', error);
      alert(`Transaction failed: ${error.message}`);
    }
  };

  // Clear cart handler
  const handleClearCart = () => {
    setCart([]);
    setAmountPaid('');
  };

  if (loading) {
    return (
      <div className="pos-page">
        <header className="header-bar">
          <h1 className="header-title">BuiswAIz</h1>
        </header>
        <div className="pos-main-section">
          <aside className="sidebar">
            <div className="nav-section">
              <p className="nav-header">GENERAL</p>
              <ul>
                <li onClick={() => navigate("/Dashboard")}>Dashboard</li>
                <li onClick={() => navigate("/inventory")}>Inventory</li>
                <li onClick={() => navigate("/TablePage")}>Sales</li>
                <li onClick={() => navigate("/expenses")}>Expenses</li>
                <li onClick={() => navigate("/assistant")}>AI Assistant</li>
              </ul>
              <p className="nav-header">Related</p>
              <ul>
                <li onClick={() => navigate("/supplier")}>Supplier</li>
                <li className="active">Point of Sales</li>
                <li onClick={() => navigate("/PlannedPaymentsPage")}>Planned Payment</li>
              </ul>
            </div>
          </aside>
          <div className="pos-content">
            <div className="loading-states">Loading products...</div>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="pos-page">
      <header className="header-bar">
        <h1 className="header-title">BuiswAIz</h1>
      </header>
      
      <div className="pos-main-section">
        <aside className="sidebar">
          <div className="nav-section">
            <p className="nav-header">GENERAL</p>
            <ul>
              <li onClick={() => navigate("/Dashboard")}>Dashboard</li>
              <li onClick={() => navigate("/inventory")}>Inventory</li>
              <li onClick={() => navigate("/TablePage")}>Sales</li>
              <li onClick={() => navigate("/expenses")}>Expenses</li>
              <li onClick={() => navigate("/assistant")}>AI Assistant</li>
            </ul>
            <p className="nav-header">RELATED</p>
            <ul>
              <li onClick={() => navigate("/supplier")}>Supplier</li>
              <li className="active">Point of Sales</li>
              <li onClick={() => navigate("/PlannedPaymentsPage")}>Planned Payment</li>
            </ul>
          </div>
        </aside>

        <div className="pos-content">
          <ItemsPanel
            products={products}
            searchTerm={searchTerm}
            setSearchTerm={setSearchTerm}
            selectedCategory={selectedCategory}
            setSelectedCategory={setSelectedCategory}
            categories={categories}
            onAddToCart={addToCart}
          />

          <SellingPanel
            cart={cart}
            amountPaid={amountPaid}
            setAmountPaid={setAmountPaid}
            onUpdateQuantity={updateQuantity}
            onRemoveFromCart={removeFromCart}
            onCompleteTransaction={completeTransaction}
            onClearCart={handleClearCart}
          />
        </div>
      </div>
      
      <SalesSuccessModal
        isOpen={showSuccessModal}
        onClose={() => setShowSuccessModal(false)}
        orderData={orderData}
      />
    </div>
  );
};

export default PointOfSales;