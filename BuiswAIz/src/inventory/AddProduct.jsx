import React, { useEffect, useState } from "react";
import "../stylecss/AddProduct.css";
import imageCompression from "browser-image-compression";

const AddProduct = ({ onClose, user }) => {
  const [suppliers, setSuppliers] = useState([]);
  const [imageFile, setImageFile] = useState(null);
  const [formError, setFormError] = useState("");
  const [isSubmitting, setIsSubmitting] = useState(false);

  // Product-level state
  const [formData, setFormData] = useState({
    productname: "",
    description: "",
    suppliername: "",
  });

  // Dynamic category state
  const [categories, setCategories] = useState([
    {
      color: "",
      agesize: "",
      cost: "",
      price: "",
      currentstock: "",
      reorderpoint: "",
    },
  ]);

  useEffect(() => {
    const fetchSuppliers = async () => {
      try {
        const response = await fetch(`${import.meta.env.VITE_BACKEND_URL}/api/get-suppliers`);
        const data = await response.json();
        setSuppliers(data);
      } catch (err) {
        console.error(err);
      }
    };
    fetchSuppliers();
  }, []);

  const handleChange = (e) => {
    const { name, value } = e.target;
    setFormData((prev) => ({ ...prev, [name]: value }));
  };

  const handleCategoryChange = (index, e) => {
    const { name, value } = e.target;
    const updated = [...categories];
    updated[index][name] = value;
    setCategories(updated);
  };

  const addCategoryRow = () => {
    setCategories([
      ...categories,
      { color: "", agesize: "", cost: "", price: "", currentstock: "", reorderpoint: "" },
    ]);
  };

  const removeCategoryRow = (index) => {
    const updated = [...categories];
    updated.splice(index, 1);
    setCategories(updated);
  };

  const handleImageSelect = async (e) => {
    const file = e.target.files[0];
    if (!file) return;

    const options = { maxSizeMB: 0.1, maxWidthOrHeight: 500, useWebWorker: true };
    try {
      const compressedFile = await imageCompression(file, options);
      compressedFile.name = file.name;
      setImageFile(compressedFile);
    } catch (err) {
      console.error("Image compression failed:", err);
    }
  };

  const handleSubmit = async () => {
    if (isSubmitting) return;
    setIsSubmitting(true);
    setFormError("");

    if (!formData.productname || !formData.description || !formData.suppliername) {
      setFormError("Please fill in all product fields.");
      setIsSubmitting(false);
      return;
    }

   // Validate category fields
  const hasValidCategory = categories.some(
    (cat) =>
      cat.color.trim() !== "" ||
      cat.agesize.trim() !== "" ||
      cat.cost !== "" ||
      cat.price !== "" ||
      cat.currentstock !== "" ||
      cat.reorderpoint !== ""
  );

  if (!hasValidCategory) {
    setFormError("Please fill in at least one complete category.");
    setIsSubmitting(false);
    return;
  }


    try {
      const form = new FormData();
      form.append("productname", formData.productname);
      form.append("description", formData.description);
      form.append("suppliername", formData.suppliername);
      form.append("userid", user.userid);

      // categories as JSON string
      form.append("categories", JSON.stringify(categories));

      // Append image if selected
      if (imageFile) form.append("image", imageFile);

      const response = await fetch(`${import.meta.env.VITE_BACKEND_URL}/api/add-product`, {
        method: "POST",
        body: form,
      });

      const data = await response.json();
      if (!response.ok) {
        setFormError(data.error || "Failed to add product");
      } else {
        onClose();
      }
    } catch (err) {
      console.error(err);
      setFormError("Server error");
    } finally {
      setIsSubmitting(false);
    }
  };


  return (
    <div className="Addmodal-overlay">
      <div className="Addmodal-content slide-up">
        <div className="modal-header">
          <button className="back-btn" onClick={onClose}>
            ←
          </button>
          <h2>New Product</h2>
          <div className="modal-actions">
            <button className="create-btn" onClick={handleSubmit} disabled={isSubmitting}>
              {isSubmitting ? "Creating..." : "Create Item"}
            </button>
          </div>
        </div>

        <div className="modal-body">
          <div className="image-upload">
            <label className="upload-box">
              {imageFile ? (
                <img src={URL.createObjectURL(imageFile)} alt="Preview" className="preview-image" />
              ) : (
                "Click to upload image"
              )}
              <input type="file" accept="image/*" onChange={handleImageSelect} style={{ display: "none" }} />
            </label>
          </div>

          <div className="product-fields">
            <label>Product title</label>
            <input name="productname" placeholder="Product Name" value={formData.productname} onChange={handleChange} />
            <textarea name="description" placeholder="Description" value={formData.description} onChange={handleChange} />
            <select name="suppliername" value={formData.suppliername} onChange={handleChange}>
              <option value="">Select Supplier</option>
              {suppliers.map((s) => (
                <option key={s.supplierid} value={s.suppliername}>
                  {s.suppliername}
                </option>
              ))}
            </select>
          </div>
        </div>

        <div className="categories-section">
          <h3 className="section-title">Categories</h3>
          {categories.map((cat, index) => (
            <div key={index} className="category-card">
              <div className="category-fields">
                <input name="color" placeholder="Color" value={cat.color} onChange={(e) => handleCategoryChange(index, e)} />
                <select name="agesize" value={cat.agesize} onChange={(e) => handleCategoryChange(index, e)}>
                  <option value="">-- Select Age/Size --</option>
                  <option value="Newborn">Newborn</option>
                  <option value="0-3 Months">0-3 Months</option>
                  <option value="3-6 Months">3-6 Months</option>
                  <option value="6-9 Months">6-9 Months</option>
                  <option value="9-12 Months">9-12 Months</option>
                  <option value="1-2 Years Old">1-2 Years Old</option>
                  <option value="3-4 Years Old">3-4 Years Old</option>
                  <option value="5 Years Old">5 years Old</option>
                </select>
                <input type="number" name="cost" placeholder="Cost" value={cat.cost} onChange={(e) => handleCategoryChange(index, e)} />
                <input type="number" name="price" placeholder="Price" value={cat.price} onChange={(e) => handleCategoryChange(index, e)} />
                <input type="number" name="currentstock" placeholder="Stock" value={cat.currentstock} onChange={(e) => handleCategoryChange(index, e)} />
                <input type="number" name="reorderpoint" placeholder="Reorder Point" value={cat.reorderpoint} onChange={(e) => handleCategoryChange(index, e)} />
              </div>
              {categories.length > 1 && (
                <button className="remove-category-btn" type="button" onClick={() => removeCategoryRow(index)}>
                  ✕ Remove
                </button>
              )}
            </div>
          ))}
          <button type="button" className="add-category-btn" onClick={addCategoryRow}>
            + Add Category
          </button>
        </div>

        {formError && <div className="productForm-warning">{formError}</div>}
      </div>
    </div>
  );
};

export default AddProduct;
