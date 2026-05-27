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
  const [user, setUser] = useState(null);
  const [userBusinessId, setUserBusinessId] = useState(null);
  const [businessInfo, setBusinessInfo] = useState(null);
  const [categories, setCategories] = useState(['All']);
  const [showSuccessModal, setShowSuccessModal] = useState(false);
  const [orderData, setOrderData] = useState({});

  // Help tooltip states
  const [showHelpItems, setShowHelpItems] = useState(false);
  const [showHelpSelling, setShowHelpSelling] = useState(false);

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

  // Fetch products from Supabase filtered by business
  const fetchProducts = useCallback(async () => {
    if (!userBusinessId) {
      return;
    }

    try {
      // FIXED: Only fetch products that belong to this business
      const { data: businessProducts, error: productsError } = await supabase
        .from('products')
        .select('productid, productname, description, image_url, businessid')
        .eq('businessid', userBusinessId); // Only get products for THIS business

      if (productsError) {
        console.error('Error fetching products:', productsError.message);
        setLoading(false);
        return;
      }

      if (!businessProducts || businessProducts.length === 0) {
        console.log('No products found for this business');
        setProducts([]);
        setLoading(false);
        return;
      }

      const productIds = businessProducts.map(p => p.productid);
      
      const { data: categories, error: categoriesError } = await supabase
        .from('productcategory')
        .select('*')
        .in('productid', productIds)
        .gt('currentstock', 0)
        .order('productcategoryid');

      if (categoriesError) {
        console.error('Error fetching categories:', categoriesError.message);
        setLoading(false);
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
          reorderpoint: cat.reorderpoint,
          businessid: product?.businessid || userBusinessId,
          categoryLabel: cat.agesize || 'Uncategorized',
          displayName: [
            product?.productname,
            cat.color && `(${cat.color})`,
            cat.agesize && `[${cat.agesize}]`
          ].filter(Boolean).join(' ')
        };
      });

      console.log(`Found ${transformedProducts.length} products for business ${userBusinessId}`);
      
      setProducts(transformedProducts);
      
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
  }, [userBusinessId]);

  useEffect(() => {
    if (userBusinessId) {
      fetchProducts();
    }
  }, [userBusinessId, fetchProducts]);

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

  const removeFromCart = (productCategoryId) => {
    setCart(cart.filter(item => item.productcategoryid !== productCategoryId));
  };

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

  // Modified: Generate unique order ID globally (not per business)
  const generateUniqueOrderId = async () => {
    try {
      // Get the highest order ID across ALL businesses (for primary key uniqueness)
      const { data, error } = await supabase
        .from('orders')
        .select('orderid')
        .order('orderid', { ascending: false })
        .limit(1);
      
      if (error) throw new Error(`Database error: ${error.message}`);
      
      // If no orders exist at all, start at 1
      if (!data || data.length === 0) return 1;
      
      const highestOrderId = parseInt(data[0].orderid, 10);
      return isNaN(highestOrderId) ? 1 : highestOrderId + 1;
    } catch (error) {
      console.error('Error generating order ID:', error);
      throw error;
    }
  };

  // Modified: Generate order code starting from ORDER-1 per business
  const generateOrderCode = async () => {
    try {
      // Get the count of orders for THIS BUSINESS ONLY
      const { count, error } = await supabase
        .from('orders')
        .select('*', { count: 'exact', head: true })
        .eq('businessid', userBusinessId);
      
      if (error) throw error;
      
      // Next order number for this business (count + 1)
      const nextOrderNumber = (count || 0) + 1;
      const orderCode = `ORDER-${nextOrderNumber}`;
      
      // Double check if this order code already exists for this business
      const { data: existing, error: checkError } = await supabase
        .from('orders')
        .select('ordercode')
        .eq('ordercode', orderCode)
        .eq('businessid', userBusinessId)
        .maybeSingle();
      
      if (checkError && checkError.code !== 'PGRST116') {
        throw checkError;
      }
      
      if (existing) {
        // If somehow exists, use fallback with timestamp
        const timestamp = Date.now().toString(36).toUpperCase();
        return `ORDER-${nextOrderNumber}-${timestamp}`;
      }
      
      return orderCode;
    } catch (error) {
      // Fallback: if error occurs, try to generate safely
      console.error('Error generating order code:', error);
      const timestamp = Date.now().toString(36).toUpperCase();
      const random = Math.random().toString(36).substring(2, 4).toUpperCase();
      return `ORDER-${timestamp}-${random}`;
    }
  };

  const completeTransaction = async (dateTimeData) => {
    if (cart.length === 0) {
      alert('Cart is empty!');
      return;
    }

    if (!amountPaid || parseFloat(amountPaid) <= 0) {
      alert('Please enter the amount paid by customer.');
      return;
    }

    if (!userBusinessId) {
      alert('Business information not found. Please refresh and try again.');
      return;
    }

    const { orderDate, orderTime } = dateTimeData;

    if (!orderDate || !orderTime) {
      alert('Please select date and time for the transaction.');
      return;
    }

    const subtotal = cart.reduce((sum, item) => sum + (item.price * item.quantity), 0);
    const total = subtotal;

    const paidAmount = parseFloat(amountPaid);
    const orderStatus = paidAmount >= total ? 'COMPLETE' : 'INCOMPLETE';
    const change = paidAmount >= total ? (paidAmount - total) : 0;

    try {
      const uniqueOrderId = await generateUniqueOrderId();
      const orderCode = await generateOrderCode();
      const orderDateTime = `${orderDate} ${orderTime}:00`;

      const orderRecord = {
        orderid: uniqueOrderId,
        ordercode: orderCode,
        totalamount: total,
        orderdate: orderDateTime,
        orderstatus: orderStatus,
        amount_paid: paidAmount,
        change: change,
        userid: user.userid,
        businessid: userBusinessId
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
      
      setOrderData({
        orderId: uniqueOrderId,
        orderCode: orderCode,
        totalAmount: total,
        amountPaid: paidAmount,
        change: change,
        status: orderStatus,
        itemCount: cart.length,
        businessName: businessInfo?.businessname
      });
      
      setCart([]);
      setAmountPaid('');
      setShowSuccessModal(true);
      await fetchProducts();
      
    } catch (error) {
      console.error('Transaction error:', error);
      alert(`Transaction failed: ${error.message}`);
    }
  };

  const handleClearCart = () => {
    setCart([]);
    setAmountPaid('');
  };

  if (loading) {
    return (
      <div className="pos-page">
        <header className="header-bar">
          <h1 className="header-title">Bake-keri</h1>
        </header>
        <div className="pos-main-section">
          <aside className="sidebar">
            <div className="nav-section">
              <p className="nav-header">GENERAL</p>
              <ul>
                <li onClick={() => navigate("/Dashboard")}>Dashboard</li>
                <li onClick={() => navigate("/inventory")}>Inventory</li>
                <li onClick={() => navigate("/TablePage")}>Sales</li>
                {/* <li onClick={() => navigate("/expenses")}>Expenses</li> */}
                <li onClick={() => navigate("/assistant")}>AI Assistant</li>
                {/* COMMENTED OUT: Business Maturity link removed (feature did not meet team/advisor standards) */}
                {/* <li onClick={() => navigate("/maturity-report")}>Business Maturity</li> */}
              </ul>
              <p className="nav-header">RELATED</p>
              <ul>
                <li onClick={() => navigate("/supplier")}>Supplier</li>
                <li className="active">Point of Sales</li>
                <li onClick={() => navigate("/online-orders")}>Online Orders</li>
                {/* <li onClick={() => navigate("/PlannedPaymentsPage")}>Planned Payment</li> */}
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
        <h1 className="header-title">Bake-keri</h1>
        {businessInfo && (
          <div className="business-info-header">
            <span className="business-name">{businessInfo.businessname}</span>
            {businessInfo.businesscode && (
              <span className="business-code">Code: {businessInfo.businesscode}</span>
            )}
          </div>
        )}
      </header>
      
      <div className="pos-main-section">
        <aside className="sidebar">
          <div className="nav-section">
            <p className="nav-header">GENERAL</p>
            <ul>
              <li onClick={() => navigate("/Dashboard")}>Dashboard</li>
              <li onClick={() => navigate("/inventory")}>Inventory</li>
              <li onClick={() => navigate("/TablePage")}>Sales</li>
              {/* <li onClick={() => navigate("/expenses")}>Expenses</li> */}
              <li onClick={() => navigate("/assistant")}>AI Assistant</li>
              {/* COMMENTED OUT: Business Maturity link removed (feature did not meet team/advisor standards) */}
              {/* <li onClick={() => navigate("/maturity-report")}>Business Maturity</li> */}
            </ul>
            <p className="nav-header">RELATED</p>
            <ul>
              <li onClick={() => navigate("/supplier")}>Supplier</li>
              <li className="active">Point of Sales</li>
              <li onClick={() => navigate("/online-orders")}>Online Orders</li>
              {/* <li onClick={() => navigate("/PlannedPaymentsPage")}>Planned Payment</li> */}
            </ul>
          </div>
        </aside>

        <div className="pos-content">
          {products.length === 0 ? (
            <div className="no-products-message">
              <h3>No Products Available</h3>
              <p>Your business ({businessInfo?.businessname}) doesn't have any products in stock yet.</p>
              <button onClick={() => navigate("/inventory")} className="go-to-inventory-btn">
                Go to Inventory
              </button>
            </div>
          ) : (
            <>
              <div className="pos-panel-wrapper">
                <div className="pos-panel-header">
                  <div className="header-left-dash">
                    <h3>Available Items</h3>
                    <div className="help-wrapper-dash">
                      <button 
                        className="help-button-dash"
                        onClick={() => setShowHelpItems(!showHelpItems)}
                        aria-label="Help"
                      >
                        ?
                      </button>
                      {showHelpItems && (
                        <div className="help-box-dash">
                          <div className="help-arrow-dash"></div>
                          
                          <div className="help-content-dash">
                            <p>Gen TIPS</p>
                          </div>
                          
                          <div className="help-separator-dash"></div>
                          
                          <div className="help-content-dash">
                            <p>AI</p>
                          </div>
                        </div>
                      )}
                    </div>
                  </div>
                </div>
                <ItemsPanel
                  products={products}
                  searchTerm={searchTerm}
                  setSearchTerm={setSearchTerm}
                  selectedCategory={selectedCategory}
                  setSelectedCategory={setSelectedCategory}
                  categories={categories}
                  onAddToCart={addToCart}
                />
              </div>

              <div className="pos-panel-wrapper">
                <div className="pos-panel-header">
                  <div className="header-left-dash">
                    <h3>Checkout</h3>
                    <div className="help-wrapper-dash">
                      <button 
                        className="help-button-dash"
                        onClick={() => setShowHelpSelling(!showHelpSelling)}
                        aria-label="Help"
                      >
                        ?
                      </button>
                      {showHelpSelling && (
                        <div className="help-box-dash">
                          <div className="help-arrow-dash"></div>
                          
                          <div className="help-content-dash">
                            <p>Gen TIPS</p>
                          </div>
                          
                          <div className="help-separator-dash"></div>
                          
                          <div className="help-content-dash">
                            <p>AI</p>
                          </div>
                        </div>
                      )}
                    </div>
                  </div>
                </div>
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
            </>
          )}
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