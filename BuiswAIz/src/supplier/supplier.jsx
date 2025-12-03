import React, { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { fetchSupplier, fetchSupplierWithProducts } from "../supplier/fetchsuppliertable";
import { supabase } from "../supabase";
import "../stylecss/supplier.css";
import AddSupplier from "../supplier/AddSupplier";
import ViewSupplier from "../supplier/ViewSupplier";

const Supplier = () => {
  const navigate = useNavigate();
  const [suppliers, setSuppliers] = useState([]);
  const [user, setUser] = useState(null);
  const [searchTerm, setSearchTerm] = useState("");
  const [showModal, setShowModal] = useState(false);
  const [selectedSupplier, setSelectedSupplier] = useState(null);
  const [supplierStats, setSupplierStats] = useState([]);
  const [orders, setOrders] = useState([]);
  const [showReceiveModal, setShowReceiveModal] = useState(false);
  const [selectedOrder, setSelectedOrder] = useState(null);
  const [formError, setFormError] = useState("");
  const [newCost, setNewCost] = useState(0);

  // Load suppliers
  const loadSupplier = async () => {
    try {
      const data = await fetchSupplier();
      setSuppliers(data);
    } catch (err) {
      console.error("Error loading suppliers:", err);
    }
  };

  // Load supplier stats
  const loadSupplierStats = async () => {
    try {
      const data = await fetchSupplierWithProducts();
      setSupplierStats(data);
    } catch (err) {
      console.error("Error loading supplier stats:", err);
    }
  };

  // Load orders
  const loadOrders = async () => {
      const { data, error } = await supabase
        .from("purchase_orders")
        .select(`
          purchaseorderid,
          productid,
          productcategoryid,
          supplierid,
          unit_cost,
          total_cost,
          order_qty,
          status,
          received_at,
          products ( productname ),
          productcategory ( color, agesize )
        `)
        .order('created_at', { ascending: false });

      if (error) throw error;

      setOrders(data);
  };

  // Handle marking order as received
  const handleReceiveOrder = (order) => {
    setSelectedOrder(order);
    setShowReceiveModal(true);
  };

  const confirmReceiveOrder = async (batchCode, newCost, newPrice) => {
    try {
      const receivedAt = new Date();

      // Insert into restockstorage first
      const { error: restockError } = await supabase
        .from("restockstorage")
        .insert({
          productid: selectedOrder.productid,
          productcategoryid: selectedOrder.productcategoryid,
          supplierid: selectedOrder.supplierid,
          new_stock: selectedOrder.order_qty,
          new_cost: newCost,
          new_price: newPrice,
          batchCode,
          datereceived: receivedAt,
          created_at: receivedAt
        });

      if (restockError) throw restockError;

      //Insert into expenses
      const actualPayment = newCost * selectedOrder.order_qty;
      const { error: expenseError } = await supabase
        .from("expenses")
        .insert({
          user_id: user?.userid || null,
          occurred_on: receivedAt,
          category_id: "5e4b2625-86ba-4066-adaa-4657700c118c",
          amount: actualPayment,
          notes: `Payment to supplier ${selectedOrder.supplierid} for product ${selectedOrder.products?.productname}`,
          status: "cleared",
        });

      if (expenseError) throw expenseError;

      // Delete the order from purchase_orders
      const { error: deleteError } = await supabase
        .from("purchase_orders")
        .delete()
        .eq("purchaseorderid", selectedOrder.purchaseorderid);

      if (deleteError) throw deleteError;

      // Cleanup
      setShowReceiveModal(false);
      setSelectedOrder(null);
      loadOrders();

    } catch (err) {
      console.error("Error processing received order:", err);
    }
  };


  useEffect(() => {
    const getUser = async () => {
      const { data: { user }, error } = await supabase.auth.getUser();
      if (error || !user) {
        window.location.href = "/";
        return;
      }

      const { data: profile, error: profileError } = await supabase
        .from("systemuser")
        .select("*")
        .eq("userid", user.id)
        .single();

      if (profileError) {
        console.error("Error fetching user profile:", profileError);
        return;
      }

      setUser(profile);
    };

    getUser();
    loadSupplier();
    loadSupplierStats();
    loadOrders();
  }, []);

  const filteredSuppliers = suppliers.filter(s =>
    s.suppliername.toLowerCase().includes(searchTerm.toLowerCase())
  );

  const maxDefects = suppliers.length > 0
    ? Math.max(...suppliers.map(s => s.defectreturned || 0))
    : 1;
  return (
    <div className="supplier-page">
      <header className="header-bar">
        <h1 className="header-title">BuiswAIz</h1>
      </header>
      <div className="main-section">
        {/* Sidebar */}
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
              <li className="active">Supplier</li>
              <li onClick={() => navigate("/pos")}>Point of Sales</li>
              <li onClick={() => navigate("/PlannedPaymentsPage")}>Planned Payment</li>
            </ul>
          </div>
        </aside>

        {/* Main Content */}
        <div className="S-main-content">
          {/* Supplier Table */}
          <div className="supplier-panel">
            <div className="panel-header">
              <h2 className="panel-title">Supplier</h2>
              <div className="panel-actions">
                <input
                  id="supplierSearch"
                  className="supplier-search"
                  type="text"
                  placeholder="Search"
                  value={searchTerm}
                  onChange={(e) => setSearchTerm(e.target.value)}
                />
                <button className="add-supplier-button" onClick={() => setShowModal(true)}>
                  + Add Supplier
                </button>
              </div>
            </div>

            <div className="supplier-container">
              <table>
                <thead>
                  <tr>
                    <th>Name</th>
                    <th>Code</th>
                    <th>ContactName</th>
                    <th>PhoneNumber</th>
                    <th>Email</th>
                    <th>Address</th>
                    <th>Status</th>
                  </tr>
                </thead>
                <tbody>
                  {filteredSuppliers.map((supplier, i) => (
                    <tr
                      key={supplier.supplierid || i}
                      onClick={() => setSelectedSupplier(supplier)}
                      style={{ cursor: "pointer" }}
                    >
                      <td>{supplier.suppliername}</td>
                      <td>{supplier.supplierid}</td>
                      <td>{supplier.contactperson}</td>
                      <td>{supplier.phonenumber}</td>
                      <td>{supplier.supplieremail}</td>
                      <td>{supplier.address}</td>
                      <td className={supplier.supplierstatus === "Active" ? "status-active" : "status-inactive"}>
                        {supplier.supplierstatus}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <div className="supplier-orders-section">
              <h3>Supplier Orders</h3>

              <div className="orders-tables-wrapper">
                <div className="orders-tables-grid">
                  
                  {/* Left Table: Pending Orders */}
                  <div className="orders-table-wrapper">
                    <h4>Pending & Confirmed Orders</h4>
                    <div className="table-scroll">
                      <table>
                        <thead>
                          <tr>
                            <th>Supplier</th>
                            <th>Product</th>
                            <th>Category</th>
                            <th>Qty</th>
                            <th>Status</th>
                            <th>Action</th>
                          </tr>
                        </thead>
                        <tbody>
                          {orders
                            .filter(order => order.status === "Pending" || order.status === "Confirmed") // Pending & Confirmed
                            .map(order => {
                              const supplierName = suppliers.find(s => s.supplierid === order.supplierid)?.suppliername || "Unknown";
                              return (
                                <tr key={order.purchaseorderid}>
                                  <td>{supplierName}</td>
                                  <td>{order.products?.productname}</td>
                                  <td>{order.productcategory?.color} {order.productcategory?.agesize}</td>
                                  <td>{order.order_qty}</td>
                                  <td className={`status-${order.status.toLowerCase()}`}>{order.status}</td>
                                  <td>
                                    {order.status === "Confirmed" ? (
                                      <button className="mark-received-btn" onClick={() => handleReceiveOrder(order)}>
                                        Mark as Received
                                      </button>
                                    ) : (
                                      "-"
                                    )}
                                  </td>
                                </tr>
                              );
                            })}
                        </tbody>
                      </table>
                    </div>
                  </div>


                  {/* Right Table: Rejected Orders */}
                  <div className="orders-table-wrapper">
                    <h4>Rejected Orders</h4>
                    <div className="table-scroll">
                      <table>
                        <thead>
                          <tr>
                            <th>Supplier</th>
                            <th>Product</th>
                            <th>Category</th>
                            <th>Qty</th>
                            <th>Status</th>
                          </tr>
                        </thead>
                        <tbody>
                          {orders
                            .filter(order => order.status === "Rejected")
                            .map(order => {
                              const supplierName = suppliers.find(s => s.supplierid === order.supplierid)?.suppliername || "Unknown";
                              return (
                                <tr key={order.purchaseorderid}>
                                  <td>{supplierName}</td>
                                  <td>{order.products?.productname}</td>
                                  <td>{order.productcategory?.color} {order.productcategory?.agesize}</td>
                                  <td>{order.order_qty}</td>
                                  <td className={`status-${order.status.toLowerCase()}`}>{order.status}</td>
                                </tr>
                              );
                            })}
                        </tbody>
                      </table>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </div>

          {/* Right Panel */}
          <div className="S-right-panel">
            <div className="S-user-info-card">
              <div className="S-user-left">
                <div className="S-user-avatar"/>
                <div className="S-user-username">{user ? user.username : "Loading..."}</div>
              </div>
              <button
                className="logout-button"
                onClick={async () => {
                  await supabase.auth.signOut();
                  localStorage.removeItem("userProfile");
                  localStorage.removeItem('lastActive');
                  window.location.href = "/login";
                }}
              >
                ⏻
              </button>
            </div>

            <div className="supply-returned-panel">
              <h3>Product Returned to Supplier</h3>
              <div className="returned-container">
                {suppliers
                .sort((a, b) => (b.defectreturned || 0) - (a.defectreturned || 0))
                .map(s => (
                  <div key={s.supplierid} className="returned-card">
                    <div className="supplier-info">
                      <span className="supplier-name">{s.suppliername}</span>
                      <span className="returned-count">{s.defectreturned || 0}</span>
                    </div>
                    <div className="progress-bar">
                      <div className="progress-fill" style={{ width: `${((s.defectreturned || 0) / maxDefects) * 100}%` }}></div>
                    </div>
                  </div>
                ))}
              </div>
            </div>

            <div className="supplier-stats-panel">
              <h3>Supplier Product Stats</h3>
              <div className="S-stats-container">
                {supplierStats.map((s, i) => (
                  <div key={i} className="S-stats-row">
                    <span className="supplier-name">{s.suppliername}</span>
                    <span className="supplier-status">{s.supplierstatus}</span>
                    <span className="product-count">{s.totalproducts}</span>
                  </div>
                ))}
              </div>
            </div>
          </div>
        </div>
      </div>

      {/* Modals */}
      {showModal && (
        <AddSupplier
          onClose={() => {
            setShowModal(false);
            loadSupplier();
            loadSupplierStats();
          }}
          user={user}
        />
      )}

      {selectedSupplier && (
        <ViewSupplier
          supplier={selectedSupplier}
          onClose={() => {
            setSelectedSupplier(null);
            loadSupplier();
            loadSupplierStats();
          }}
          user={user}
        />
      )}

      {showReceiveModal && selectedOrder && (
        <div className="sup-modal-overlay">
          <div className="modal-box">
            <h3>Receive Order</h3>
            <p>Product: {selectedOrder.products?.productname}</p>
            <p>Category: {selectedOrder.productcategory?.color} {selectedOrder.productcategory?.agesize}</p>
            <p>Qty: {selectedOrder.order_qty}</p>
            <p>Old Cost per Unit: {selectedOrder.unit_cost}</p>
            <p>Inital Total Cost: {selectedOrder.total_cost}</p>
            <label>Batch Code: <input type="text" id="batchCode" /></label>
            <label>New Cost per Unit: 
              <input type="number" 
                value={newCost}
                min="0" 
                onChange={(e) => setNewCost(parseFloat(e.target.value))}/>
            </label>
            <label>Actual Payment</label> <input type="number" value={(newCost * selectedOrder.order_qty).toFixed(2)} readOnly/>
            <label>New Price: <input type="number" id="newPrice" min="0"/></label>
            <button className ="received-btn"onClick={() => {
              const batchCode = document.getElementById("batchCode").value;
              const newPrice = parseFloat(document.getElementById("newPrice").value);
              confirmReceiveOrder(batchCode, newCost, newPrice);
            }}>Confirm Receive</button>
            <button className="cancel-sup-btn"onClick={() => { setShowReceiveModal(false); setSelectedOrder(null); }}>Cancel</button>
          </div>
        </div>
      )}
    </div>
  );
};

export default Supplier;
