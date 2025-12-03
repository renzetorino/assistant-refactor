import React, { useEffect, useState } from "react";
import { fetchProducts } from "../inventory/fetchproducttable";
import "../stylecss/inventory.css";
import { supabase } from "../supabase";
import AddProduct from "../inventory/AddProduct";
import ViewProduct from "../inventory/ViewProduct";
import AddDefect from "../inventory/AddDefect";
import RestockStorage from "../inventory/RestockStorage";
import { useNavigate } from "react-router-dom";
import { fetchLowStockProducts } from "../inventory/fetchLowStockProduct";
import { fetchDefectiveItems } from "../inventory/fetchdefectitem";
import DefectivePanel from "../inventory/DefectivePanel";
import InheritedBatches from "../inventory/inheritedBatches";
import ProductAvailability from "../inventory/ProductAvailability";   


const Inventory = () => {
  const [products, setProducts] = useState([]);
  const [showModal, setShowModal] = useState(false);
  const [showDefectModal, setShowDefectModal] = useState(false);
  const [searchTerm, setSearchTerm] = useState("");
  const [selectedProduct, setSelectedProduct] = useState(null);
  const [lowStockProducts, setLowStockProducts] = useState([]);
  const [defectiveItems, setDefectiveItems] = useState([]);
  const [user, setUser] = useState(null);
  const [activityLogs, setActivityLogs] = useState([]);
  const navigate = useNavigate();
  const [restockStorage, setrestockStorage] = useState(false);

  const loadProducts = async () => {
    try {
      const data = await fetchProducts();
      setProducts(data);
    } catch (err) {
      console.error("Error loading products", err);
    }
    
  };
  const loadLowStock = async () => {
    const data = await fetchLowStockProducts();
    setLowStockProducts(data);
  };

  const loadDefectiveItems = async () => {
    const data = await fetchDefectiveItems();
    setDefectiveItems(data);
  };

  const loadActivityLogs = async () => {
    const { data, error } = await supabase
      .from("activitylog")
      .select("*, systemuser(username)")
      .order("created_at", { ascending: false });

    if (error) {
      console.error("Failed to fetch activity logs:", error);
      return;
    }

    setActivityLogs(data.slice(0, 50)); 

    
    if (data.length > 50) {
      const logsToDelete = data.slice(50); 
      const idsToDelete = logsToDelete.map(log => log.activity_id); 

      const { error: deleteError } = await supabase
        .from("activitylog")
        .delete()
        .in("activity_id", idsToDelete);

      if (deleteError) {
        console.error("Failed to delete old logs:", deleteError);
      } else {
        console.log(`Deleted ${idsToDelete.length} old logs.`);
      }
    }
  };


  useEffect(() => {
    const getUser = async () => {
    const { data: { user }, error } = await supabase.auth.getUser();
    if (error || !user) {
      window.location.href = '/'; // redirect to login
      return;
    }

    const { data: profile, error: profileError } = await supabase
      .from('systemuser')
      .select('*')
      .eq('userid', user.id)
      .single();

    if (profileError) {
      console.error("Error fetching user profile:", profileError);
      return;
    }

    setUser(profile);
  };
    getUser();
    loadProducts();
    loadLowStock();
    loadDefectiveItems();
    loadActivityLogs();
    const interval = setInterval(() => {
      loadLowStock();
      loadActivityLogs();
      loadDefectiveItems();
    }, 5000);

    return () => clearInterval(interval); 
  }, []);

  const filteredProducts = products.filter((product) =>
    product.productname.toLowerCase().includes(searchTerm.toLowerCase())
  );

  return (
    <div className="inventory-page">
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
              <li className="active">Inventory</li>
              <li onClick={() => navigate("/TablePage")}>Sales</li>
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

        {/* Main Content */}
        <div className="I-main-content">
          <div className="product-panel">
            <div className="panel-header">
              <h2 className="panel-title">Inventory</h2>
              <div className="panel-actions">
                <input id="inventorySearch" className="inventory-search" type="text" placeholder="Search" value={searchTerm} onChange={(e) => setSearchTerm(e.target.value)} />
                <button className="restock-storage-button" onClick={() => setrestockStorage(true)}> Restock Storage</button> 
                <button className="add-product-button" onClick={() => setShowModal(true)}>
                  + Add Product
                </button>
              </div>
            </div>

            <div className="inventory-container">
              <table>
                <thead>
                  <tr>
                    <th>Name</th>
                    <th>Description</th>
                    <th>Supplier</th>
                    <th>Image</th>
                  </tr>
                </thead>
                <tbody>
                  {filteredProducts.map((product, i) => (
                    <tr
                      key={product.productid || i}
                      onClick={() => setSelectedProduct(product)}
                      style={{ cursor: "pointer" }}
                    >
                      <td>{product.productname}</td>
                      <td>{product.description}</td>
                      <td>{product.suppliers?.suppliername || "Unknown"}</td>
                      <td>
                        <div className="image-cell">
                          {product.image_url ? (
                            <img
                              src={product.image_url}
                              alt="Product"
                              className="I-product-thumbnail"
                            />
                          ) : (
                            <div className="img-placeholder" />
                          )}
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <InheritedBatches user={user}/>
          </div>
                            
          {/* Right Panel */}
          <div className="I-right-panel">
            <div className="I-user-info-card">
              <div className="I-user-left">
                <div className="I-user-avatar" />
                <div className="I-user-username">
                  {user ? user.username : "Loading..."}
                </div>
              </div>
                <button
                  className="logout-button"
                  onClick={async () => {
                    await supabase.auth.signOut();
                    localStorage.removeItem("userProfile"); // optional
                    localStorage.removeItem('lastActive');
                    window.location.href = "/login"; // send back to login
                  }}
                >
                  ⏻
              </button>
            </div>

            <ProductAvailability lowStockProducts={lowStockProducts}/>

            <DefectivePanel 
              defectiveItems={defectiveItems} 
              user={user} 
              loadDefectiveItems={loadDefectiveItems}
              onAddDefect={() => setShowDefectModal(true)}
            />

          </div>
        </div>
      </div>

      {restockStorage && (
        <div className="restock-container">
          <RestockStorage onClose={() => setrestockStorage(false)} 
            user={user}
          />
        </div>
      )}

      {showModal && (
        <AddProduct
          onClose={() => {
            setShowModal(false);
            loadProducts();
            loadActivityLogs();
          }}
           user={user}
        />
      )}

      {selectedProduct && (
        <ViewProduct
          product={selectedProduct}
          onClose={() => {
            setSelectedProduct(null);
            loadProducts();
          }}
          user={user}
        />
      )}

      {showDefectModal && (
        <AddDefect
          onClose={() => {
            setShowDefectModal(false);
            loadDefectiveItems();
            loadProducts();
          }}
          user={user}
        />
      )}
    </div>
  );
};

export default Inventory;