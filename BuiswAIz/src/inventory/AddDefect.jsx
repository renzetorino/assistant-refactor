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

  /* ================================
     FETCH PRODUCTS (BUSINESS SAFE)
     ================================ */
  useEffect(() => {
    const fetchProducts = async () => {
      try {
        if (!user?.userid) return;

        const res = await fetch(
          `${import.meta.env.VITE_BACKEND_URL}/api/products?userid=${user.userid}`
        );

        const data = await res.json();

        if (!res.ok || !Array.isArray(data)) {
          console.error("Products fetch failed:", data);
          setProducts([]);
          return;
        }

        setProducts(data);
      } catch (err) {
        console.error("Error fetching products:", err);
        setProducts([]);
      }
    };

    fetchProducts();
  }, [user]);

  /* ======================================
     FETCH CATEGORIES WHEN PRODUCT CHANGES
     ====================================== */
  useEffect(() => {
    if (!form.productid) {
      setCategories([]);
      setSelectedCategoryId("");
      return;
    }

    const fetchCategories = async () => {
      try {
        const res = await fetch(
          `${import.meta.env.VITE_BACKEND_URL}/api/categories/${form.productid}`
        );
        const data = await res.json();

        if (!res.ok || !Array.isArray(data)) {
          console.error("Categories fetch failed:", data);
          setCategories([]);
          return;
        }

        setCategories(data);
        setSelectedCategoryId("");
      } catch (err) {
        console.error("Error fetching categories:", err);
        setCategories([]);
      }
    };

    fetchCategories();
  }, [form.productid]);

  /* ======================
     FORM HANDLERS
     ====================== */
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
      const res = await fetch(
        `${import.meta.env.VITE_BACKEND_URL}/api/add-defective-item`,
        {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            productid,
            productcategoryid: selectedCategoryId,
            quantity: parseInt(quantity),
            status,
            defectdescription,
            reporteddate,
            userid: user.userid, // 🔥 REQUIRED
          }),
        }
      );

      const data = await res.json();

      if (!res.ok) {
        const msg =
          typeof data.error === "string"
            ? data.error
            : data.error?.message || "Failed to add defective item.";
        setFormError(msg);
      } else {
        onClose();
      }
    } catch (err) {
      console.error("Server error:", err);
      setFormError("Server error. Please try again.");
    } finally {
      setIsSubmitting(false);
    }
  };

  /* ======================
     RENDER
     ====================== */
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
          {/* Product */}
          <div className="form-group">
            <label>Product</label>
            <select name="productid" value={form.productid} onChange={handleChange}>
              <option value="">Select Product</option>
              {products.map((p) => (
                <option key={p.productid} value={p.productid}>
                  {p.productname}
                </option>
              ))}
            </select>
          </div>

          {/* Categories */}
          {categories.length > 0 && (
            <div className="category-cards-container">
              <label>Select Category</label>
              <div className="category-cards">
                {categories.map((cat) => (
                  <div
                    key={cat.productcategoryid}
                    className={`category-card ${
                      selectedCategoryId === cat.productcategoryid ? "selected" : ""
                    }`}
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

          <div className="form-group">
            <label>Quantity</label>
            <input type="number" name="quantity" min="1" value={form.quantity} onChange={handleChange} />
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
            <textarea
              name="defectdescription"
              rows="3"
              value={form.defectdescription}
              onChange={handleChange}
            />
          </div>

          {formError && <div className="productForm-warning">{formError}</div>}
        </div>
      </div>
    </div>
  );
};

export default AddDefect;
