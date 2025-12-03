import React, { useEffect, useState } from "react";
import "../stylecss/AddDefect.css";

const AddDefect = ({ onClose, user }) => {
  const [products, setProducts] = useState([]);
  const [categories, setCategories] = useState([]);
  const [selectedCategoryId, setSelectedCategoryId] = useState("");
  const [form, setForm] = useState({
    productid: "",
    quantity: "",
    status: "In-Process",
    defectdescription: "",
    reporteddate: "",
  });
  const [formError, setFormError] = useState("");
  const [isSubmitting, setIsSubmitting] = useState(false);

  // Fetch products on mount
  useEffect(() => {
    const fetchProducts = async () => {
      try {
        const res = await fetch(`${import.meta.env.VITE_BACKEND_URL}/api/products`);
        const data = await res.json();
        setProducts(data);
      } catch (err) {
        console.error("Error fetching products:", err);
      }
    };
    fetchProducts();
  }, []);

  // Fetch categories whenever a product is selected
  useEffect(() => {
    if (!form.productid) {
      setCategories([]);
      setSelectedCategoryId("");
      return;
    }

    const fetchCategories = async () => {
      try {
        const res = await fetch(`${import.meta.env.VITE_BACKEND_URL}/api/categories/${form.productid}`);
        const data = await res.json();
        setCategories(data);
        setSelectedCategoryId(""); // Reset selected category
      } catch (err) {
        console.error("Error fetching categories:", err);
      }
    };
    fetchCategories();
  }, [form.productid]);

  const handleChange = (e) => {
    const { name, value } = e.target;
    setForm((prev) => ({ ...prev, [name]: value }));
  };

  const handleSubmit = async () => {
    setFormError("");
    if (isSubmitting) return;

    const { productid, quantity, status, reporteddate, defectdescription } = form;
    if (!productid || !selectedCategoryId || !quantity || !status || !reporteddate) {
      setFormError("Please fill all required fields and select a category.");
      return;
    }

    setIsSubmitting(true);

    try {
      const res = await fetch(`${import.meta.env.VITE_BACKEND_URL}/api/add-defective-item`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          productid,
          productcategoryid: selectedCategoryId, // updated field name
          quantity: parseInt(quantity),
          status,
          defectdescription,
          reporteddate,
          userid: user.userid,
        }),
      });

      const data = await res.json();

      if (!res.ok) {
        // If data.error is an object, use message or stringify it
        const errorMessage = typeof data.error === "string"
          ? data.error
          : data.error?.message
            ? data.error.message
            : JSON.stringify(data.error);
        setFormError(errorMessage || "Failed to add defective item.");
      } else {
        onClose(); // Close modal on success
      }
    } catch (err) {
      console.error("Server error:", err);
      setFormError("Server error. Please try again later.");
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="AddDefmodal-overlay">
      <div className="AddDefmodal-content slide-up">
        <div className="modal-header">
          <button className="back-btn" onClick={onClose}>←</button>
          <h2>Add Defective Item</h2>
          <div className="modal-actions">
            <button className="create-btn" onClick={handleSubmit} disabled={isSubmitting}>
              {isSubmitting ? "Saving..." : "Save"}
            </button>
          </div>
        </div>

        <div className="modal-body">
          {/* Product selection */}
          <div className="form-group">
            <label>Product</label>
            <select name="productid" value={form.productid} onChange={handleChange}>
              <option value="">Select Product</option>
              {products.map((p) => (
                <option key={p.productid} value={p.productid}>{p.productname}</option>
              ))}
            </select>
          </div>

          {/* Category selection as cards */}
          {categories.length > 0 && (
            <div className="category-cards-container">
              <label>Select Category</label>
              <div className="category-cards">
                {categories.map((cat) => (
                  <div
                    key={cat.productcategoryid}
                    className={`category-card ${selectedCategoryId === cat.productcategoryid ? "selected" : ""}`}
                    onClick={() => setSelectedCategoryId(cat.productcategoryid)}
                  >
                    <p><strong>Color:</strong> {cat.color}</p>
                    <p><strong>Size/Age:</strong> {cat.agesize}</p>
                    <p><strong>Stock:</strong> {cat.currentstock}</p>
                  </div>
                ))}
              </div>
            </div>
          )}

          {/* Quantity, Status, Date, Remarks */}
          <div className="form-group">
            <label>Quantity</label>
            <input type="number" name="quantity" value={form.quantity} onChange={handleChange} min="1" />
          </div>

          <div className="form-group">
            <label>Status</label>
            <select name="status" value={form.status} onChange={handleChange}>
              <option value="In-Process">In-Process</option>
              <option value="Returned">Returned</option>
            </select>
          </div>

          <div className="form-group">
            <label>Reported Date</label>
            <input type="date" name="reporteddate" value={form.reporteddate} onChange={handleChange} />
          </div>

          <div className="form-group">
            <label>Remarks</label>
            <textarea name="defectdescription" value={form.defectdescription} onChange={handleChange} rows="3" />
          </div>

          {formError && <div className="productForm-warning">{formError}</div>}
        </div>
      </div>
    </div>
  );
};

export default AddDefect;
