import React, { useEffect, useState } from "react";
import ConfirmStockModal from "./ConfirmationModals/ConfirmationStock";
import "../stylecss/RestockProduct.css";

const RestockProduct = ({ onClose, onSuccess, user }) => {
  const [suppliers, setSuppliers] = useState([]);
  const [products, setProducts] = useState([]);
  const [categories, setCategories] = useState([]);
  const [selectedCategory, setSelectedCategory] = useState(null);

  const [showConfirm, setShowConfirm] = useState(false);
  const [formError, setFormError] = useState("");
  const [isSubmitting, setIsSubmitting] = useState(false);

  const [formData, setFormData] = useState({
    productid: "",
    productcategoryid: "",
    supplierid: "",
    new_stock: "",
    new_cost: "",
    new_price: "",
    batchCode: "",
    datereceived: "",
  });

  // fetch products + suppliers
  useEffect(() => {
    const fetchSuppliers = async () => {
      const res = await fetch(`${import.meta.env.VITE_BACKEND_URL}/api/get-suppliers`);
      const data = await res.json();
      setSuppliers(data || []);
    };

    const fetchProducts = async () => {
      const res = await fetch(`${import.meta.env.VITE_BACKEND_URL}/api/products`);
      const data = await res.json();
      setProducts(data || []);
    };

    fetchSuppliers();
    fetchProducts();
  }, []);

  // when product is chosen → fetch its categories
  useEffect(() => {
    if (!formData.productid) {
      setCategories([]);
      setSelectedCategory(null);
      return;
    }

    const fetchCategories = async () => {
      const res = await fetch(
        `${import.meta.env.VITE_BACKEND_URL}/api/categories/${formData.productid}`
      );
      const data = await res.json();
      setCategories(data || []);
    };

    fetchCategories();
  }, [formData.productid]);

  const handleChange = (e) => {
    const { name, value } = e.target;
    setFormData((prev) => ({ ...prev, [name]: value }));
  };

  const formValidate = () => {
    const requiredFields = [
      "productid",
      "productcategoryid",
      "supplierid",
      "new_stock",
      "new_cost",
      "new_price",
      "batchCode",
      "datereceived",
    ];

    const isEmpty = requiredFields.some((field) => {
      const value = formData[field];
      if (typeof value === "string" && value.trim() === "") return true;
      if (["new_stock", "new_cost", "new_price"].includes(field)) {
        const numValue = Number(value);
        if (isNaN(numValue) || numValue <= 0) return true;
      }
      return false;
    });

    if (isEmpty) {
      setFormError("⚠️ Please fill in all required fields.");
      return false;
    }
    setFormError("");
    return true;
  };

  const handleAddRestock = async () => {
    if (!formValidate()) return;

    try {
      setIsSubmitting(true);

      const payload = { ...formData, user };

      const res = await fetch(`${import.meta.env.VITE_BACKEND_URL}/api/restock`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload),
      });

      const result = await res.json();

      if (!result.success) {
        setFormError("❌ " + result.error);
        return;
      }

      onSuccess();
    } catch (err) {
      console.error("Frontend restock error:", err);
      setFormError("❌ Something went wrong.");
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="restock-form-container">
      <h3>Add Restock Item</h3>
      <form className="restock-form">
        {/* Product */}
        <select
          name="productid"
          value={formData.productid}
          onChange={handleChange}
        >
          <option value="">Select Product</option>
          {products.map((p) => (
            <option key={p.productid} value={p.productid}>
              {p.productname}
            </option>
          ))}
        </select>

        {/* Category Cards */}
        {categories.length > 0 && (
          <div className="category-card-list">
            {categories.map((c) => {
              const isLow = c.currentstock <= c.reorderpoint;
              const isSelected = selectedCategory === c.productcategoryid;
              return (
                <div
                  key={c.productcategoryid}
                  className={`category-card ${isLow ? "low-stock" : ""} ${isSelected ? "selected" : ""}`}
                  onClick={() => {
                    setSelectedCategory(c.productcategoryid);
                    setFormData((prev) => ({
                      ...prev,
                      productcategoryid: c.productcategoryid,
                    }));
                  }}
                >
                  <h4>
                    {c.color} / {c.agesize}
                  </h4>
                  <p>
                    Stock: {c.currentstock} / Reorder: {c.reorderpoint}
                  </p>
                </div>
              );
            })}
          </div>
        )}

        {/* Supplier */}
        <select
          name="supplierid"
          value={formData.supplierid}
          onChange={handleChange}
        >
          <option value="">Select Supplier</option>
          {suppliers.map((s) => (
            <option key={s.supplierid} value={s.supplierid}>
              {s.suppliername}
            </option>
          ))}
        </select>

        {/* Stock + Cost + Price */}
        <input
          type="number"
          name="new_stock"
          min="1"
          placeholder="Quantity"
          value={formData.new_stock}
          onChange={handleChange}
        />
        <input
          type="number"
          name="new_cost"
          min="1"
          placeholder="Cost"
          value={formData.new_cost}
          onChange={handleChange}
        />
        <input
          type="number"
          name="new_price"
          min="1"
          placeholder="Price"
          value={formData.new_price}
          onChange={handleChange}
        />

        {/* Batch + Date */}
        <input
          type="text"
          name="batchCode"
          placeholder="Batch Code"
          value={formData.batchCode}
          onChange={handleChange}
        />
        <input
          type="date"
          name="datereceived"
          value={formData.datereceived}
          onChange={handleChange}
        />

        {/* Buttons */}
        <div className="form-buttons">
          <button
            type="button"
            onClick={() => {
              if (formValidate()) setShowConfirm(true);
            }}
            disabled={isSubmitting}
          >
            {isSubmitting ? "Saving..." : "Save"}
          </button>
          <button type="button" onClick={onClose}>
            Cancel
          </button>
        </div>

        <ConfirmStockModal
          isOpen={showConfirm}
          onConfirm={async () => {
            await handleAddRestock();
            setShowConfirm(false);
          }}
          onCancel={() => setShowConfirm(false)}
          productName={
            products.find(
              (p) => p.productid === parseInt(formData.productid)
            )?.productname
          }
        />

        {formError && <div className="form-warning">{formError}</div>}
      </form>
    </div>
  );
};

export default RestockProduct;
