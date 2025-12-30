import React, { useState, useEffect } from "react";
import ConfirmationModal from "./ConfirmationModals/ConfirmationExchange";
import "../stylecss/ProductExchange.css";

const ProductExchange = ({ onClose, user }) => {
  const BACKEND_URL = import.meta.env.VITE_BACKEND_URL;

  const [suppliers, setSuppliers] = useState([]);
  const [selectedSupplier, setSelectedSupplier] = useState("");

  const [products, setProducts] = useState([]);
  const [oldProduct, setOldProduct] = useState("");
  const [oldCategories, setOldCategories] = useState([]);
  const [oldCategory, setOldCategory] = useState("");

  const [newProduct, setNewProduct] = useState("");
  const [newCategories, setNewCategories] = useState([]);
  const [newCategory, setNewCategory] = useState("");

  const [quantity, setQuantity] = useState("");
  const [reason, setReason] = useState("");

  const [loading, setLoading] = useState(false);
  const [showConfirmModal, setShowConfirmModal] = useState(false);

  // Load suppliers on mount
  useEffect(() => {
    fetchSuppliers();
  }, []);

  const fetchSuppliers = async () => {
    try {
      const res = await fetch(`${BACKEND_URL}/api/get-suppliers`);
      const data = await res.json();
      if (Array.isArray(data)) setSuppliers(data);
      else setSuppliers([]);
    } catch (err) {
      console.error("Failed to fetch suppliers:", err);
    }
  };

  // Fetch products whenever supplier changes
  useEffect(() => {
    if (!selectedSupplier) {
      setProducts([]);
      setOldProduct("");
      setNewProduct("");
      return;
    }
    fetchProductsBySupplier(selectedSupplier);
  }, [selectedSupplier]);

  const fetchProductsBySupplier = async (supplierId) => {
    try {
      const res = await fetch(`${BACKEND_URL}/api/exchange-products?supplierid=${supplierId}`);
      const data = await res.json();
      if (Array.isArray(data)) setProducts(data);
      else setProducts([]);
    } catch (err) {
      console.error("Failed to fetch products for supplier:", err);
      setProducts([]);
    }
  };

  const fetchCategories = async (productId, type) => {
    if (!productId) return;
    try {
      const res = await fetch(`${BACKEND_URL}/api/categories/${productId}`);
      const data = await res.json();
      if (!Array.isArray(data)) return;
      if (type === "old") setOldCategories(data);
      else setNewCategories(data);
    } catch (err) {
      console.error("Failed to fetch categories:", err);
    }
  };

  // ✅ opens confirmation modal instead of submitting directly
  const handleConfirmOpen = () => {
    if (
      !selectedSupplier ||
      !oldProduct ||
      !oldCategory ||
      !newProduct ||
      !newCategory ||
      !quantity ||
      !reason
    ) {
      alert("All fields are required");
      return;
    }
    setShowConfirmModal(true);
  };

  // ✅ called only when user confirms
  const handleSubmit = async () => {
    setLoading(true);
    try {
      const res = await fetch(`${BACKEND_URL}/api/product-exchange`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          supplier_id: selectedSupplier,
          old_product: oldProduct,
          old_product_category: oldCategory,
          new_product: newProduct,
          new_product_category: newCategory,
          quantity,
          reason,
          user_id: user.userid,
        }),
      });

      const data = await res.json();
      if (data.success) {
        alert("Exchange request submitted successfully!");
        setOldProduct("");
        setOldCategory("");
        setNewProduct("");
        setNewCategory("");
        setQuantity("");
        setReason("");
        setSelectedSupplier("");
      } else {
        alert(data.error || "Failed to submit exchange");
      }
    } catch (err) {
      console.error(err);
      alert("Server error.");
    } finally {
      setLoading(false);
      setShowConfirmModal(false);
      onClose();
    }
  };

  return (
    <div className="Exc-modal-overlay">
      <div className="Exc-modal-content">
        <h2>Product Exchange</h2>
        <form>
          <label>Supplier:</label>
          <select
            value={selectedSupplier}
            onChange={(e) => setSelectedSupplier(e.target.value)}
          >
            <option value="">Select supplier</option>
            {suppliers.map((s) => (
              <option key={s.supplierid} value={s.supplierid}>
                {s.suppliername}
              </option>
            ))}
          </select>

          <label>Old Product:</label>
          <select
            value={oldProduct}
            onChange={(e) => {
              setOldProduct(e.target.value);
              fetchCategories(e.target.value, "old");
              setOldCategory("");
            }}
          >
            <option value="">Select old product</option>
            {Array.isArray(products) &&
              products.map((p) => (
                <option key={p.productid} value={p.productid}>
                  {p.productname}
                </option>
              ))}
          </select>

          <label>Old Category:</label>
          <div className="category-cards">
            {oldCategories.map((c) => (
              <div
                key={c.productcategoryid}
                className={`category-card ${
                  oldCategory === c.productcategoryid ? "selected" : ""
                }`}
                onClick={() => setOldCategory(c.productcategoryid)}
              >
                <div className="cat-color"><strong>Color: </strong>{c.color}</div>
                <div className="cat-size"><strong>Age/Size: </strong>{c.agesize}</div>
                <div className="cat-stock"><strong>Stock: </strong>{c.currentstock} </div>
              </div>
            ))}
          </div>

          <label>New Product:</label>
          <select
            value={newProduct}
            onChange={(e) => {
              setNewProduct(e.target.value);
              fetchCategories(e.target.value, "new");
              setNewCategory("");
            }}
          >
            <option value="">Select new product</option>
            {Array.isArray(products) &&
              products.map((p) => (
                <option key={p.productid} value={p.productid}>
                  {p.productname}
                </option>
              ))}
          </select>

          <label>New Category:</label>
          <div className="category-cards">
            {newCategories.map((c) => (
              <div
                key={c.productcategoryid}
                className={`category-card ${
                  newCategory === c.productcategoryid ? "selected" : ""
                }`}
                onClick={() => setNewCategory(c.productcategoryid)}
              >
                <div className="cat-color"><strong>Color: </strong>{c.color}</div>
                <div className="cat-size"><strong>Age/Size: </strong>{c.agesize}</div>
                <div className="cat-stock"><strong>Stock: </strong>{c.currentstock} </div>
              </div>
            ))}
          </div>

          <label>Quantity:</label>
          <input
            type="number"
            value={quantity}
            onChange={(e) => setQuantity(e.target.value)}
          />

          <label>Reason:</label>
          <textarea
            value={reason}
            onChange={(e) => setReason(e.target.value)}
          />

          <div className="Exc-modal-actions">
            <button type="button" onClick={onClose}>
              Close
            </button>
            <button type="button" onClick={handleConfirmOpen} disabled={loading}>
              {loading ? "Submitting..." : "Submit Exchange"}
            </button>
          </div>
        </form>
      </div>

      {showConfirmModal && (
        <ConfirmationModal
          message="Are you sure you want to submit this exchange request? This action cannot be undone and will be sent to the supplier."
          onConfirm={handleSubmit}
          onCancel={() => setShowConfirmModal(false)}
          loading={loading}
        />
      )}
    </div>
  );
};

export default ProductExchange;
