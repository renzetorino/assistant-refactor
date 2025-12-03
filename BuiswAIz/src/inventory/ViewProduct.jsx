import React, { useState, useEffect, useRef } from "react";
import "../stylecss/ViewProduct.css";
import ConfirmDeleteModal from "./ConfirmationModals/ConfirmationDelete";
import ProductItems from "./ProductItem";
import AddCategory from "./AddCategory";

const ViewProduct = ({ product, onClose, onProductUpdated, user }) => {
  const [isEditing, setIsEditing] = useState(false);
  const [form, setForm] = useState({ ...product });
  const [suppliers, setSuppliers] = useState([]);
  const [newImageFile, setNewImageFile] = useState(null);
  const fileInputRef = useRef(null);
  const [showAddCategory, setShowAddCategory] = useState(false);
  const [showConfirm, setShowConfirm] = useState(false);
  const [formError, setFormError] = useState("");
  const [deleteError, setDeleteError] = useState("");
  const [isLoading, setIsLoading] = useState(false);
  const [showItems, setShowItems] = useState(false);
  const [itemsRefreshTrigger, setItemsRefreshTrigger] = useState(0);

  const [currentProduct, setCurrentProduct] = useState(product);

  useEffect(() => {
    setCurrentProduct(product);
    setForm({ ...product });
    setFormError("");
    setDeleteError("");
    setShowItems(false);
  }, [product]);

  useEffect(() => {
    const fetchSuppliers = async () => {
      try {
        const res = await fetch(`${import.meta.env.VITE_BACKEND_URL}/api/get-suppliers`);
        const data = await res.json();
        setSuppliers(data);
      } catch (err) {
        console.error("Error fetching suppliers:", err);
      }
    };
    fetchSuppliers();
  }, []);

  const handleChange = (e) => {
    const { name, value } = e.target;
    setForm((prev) => ({ ...prev, [name]: value }));
    if (formError) setFormError("");
  };

  const handleImageClick = () => {
    if (isEditing) fileInputRef.current.click();
  };

  const handleImageChange = (e) => {
    const file = e.target.files[0];
    if (!file) return;

    const previewUrl = URL.createObjectURL(file);
    setForm((prev) => ({ ...prev, image_url: previewUrl }));
    setNewImageFile(file);
  };

  const handleUpdate = () => {
    setIsEditing(true);
    setFormError("");
  };

  const handleCancel = () => {
    setIsEditing(false);
    setForm({ ...currentProduct });
    setNewImageFile(null);
    setFormError("");
  };

  const handleSave = async () => {
    if (!form.productname?.trim() || !form.description?.trim() || !form.supplierid) {
      setFormError("Please fill in all required fields.");
      return;
    }

    setIsLoading(true);
    setFormError("");

    try {
      const formData = new FormData();
      formData.append("productid", currentProduct.productid);
      formData.append("productname", form.productname.trim());
      formData.append("description", form.description.trim());
      formData.append("supplierid", form.supplierid);
      formData.append("userid", user.userid);

      if (newImageFile) {
        formData.append("image", newImageFile); // must match multer.single("image")
      }

      const res = await fetch(`${import.meta.env.VITE_BACKEND_URL}/api/update-product`, {
        method: "POST",
        body: formData, // no JSON headers
      });

      const data = await res.json();
      if (!res.ok) throw new Error(data.error || "Failed to update product");

      setCurrentProduct({ ...form, image_url: data.imageUrl || form.image_url });
      setForm({ ...form });
      setIsEditing(false);
      setNewImageFile(null);
      if (onProductUpdated) onProductUpdated();
    } catch (err) {
      console.error(err);
      setFormError(err.message || "Failed to save product. Please try again.");
    } finally {
      setIsLoading(false);
    }
  };

   const handleDeleteClick = async () => {
    setDeleteError("");
    setIsLoading(true);

    try {
      const res = await fetch(`${import.meta.env.VITE_BACKEND_URL}/api/check-product-deletable`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ productid: currentProduct.productid }),
      });

      const data = await res.json();
      if (!res.ok) throw new Error(data.error || "Unable to check product deletability.");

      if (data.canDelete) {
        setShowConfirm(true);
      } else {
        setDeleteError("This product cannot be deleted because it has linked items or orders.");
      }
    } catch (err) {
      console.error(err);
      setDeleteError(err.message || "Failed to check product status.");
    } finally {
      setIsLoading(false);
    }
  };

  const handleDelete = async () => {
    setDeleteError("");
    setIsLoading(true);

    try {
      const res = await fetch(`${import.meta.env.VITE_BACKEND_URL}/api/delete-product`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ productid: currentProduct.productid, userid: user.userid }),
      });
      const data = await res.json();
      if (!res.ok) throw new Error(data.error || "Failed to delete product");

      if (onProductUpdated) onProductUpdated();
      onClose();
    } catch (err) {
      console.error(err);
      setDeleteError(err.message || "Failed to delete product. Please try again.");
    } finally {
      setIsLoading(false);
    }
  };

  const handleAddCategory = () => {
    setShowAddCategory(true);
  };

  const handleViewItems = () => setShowItems(true);

  const getCurrentSupplierName = () => {
    if (!isEditing && currentProduct.suppliers?.suppliername) {
      return currentProduct.suppliers.suppliername;
    }
    if (form.supplierid) {
      const supplier = suppliers.find(s => s.supplierid === form.supplierid);
      if (supplier) return supplier.suppliername;
    }
    return "No supplier selected";
  };

  return (
    <div className="view-product-overlay">
      <div className="view-product-modal">
        <div className="modal-header">
          <button
            onClick={() => (showItems ? setShowItems(false) : onClose())}
            className="back-button"
          >
            ←
          </button>
          <h2>Product</h2>
          <div className="header-actions">
            {!showItems && (
              <>
                <button
                  className="delete-button"
                  onClick={handleDeleteClick}
                  disabled={isLoading}
                >
                  🗑 Delete
                </button>
                <button
                  className="update-button"
                  onClick={handleUpdate}
                  disabled={isEditing || isLoading}
                >
                  Update Item
                </button>
              </>
            )}
            {showItems ? (
              <button className="add-category-button" onClick={handleAddCategory}>
                + Add Category
              </button>
            ) : (
              <button className="items-button" onClick={handleViewItems}>
                View Items
              </button>
            )}
          </div>
        </div>

        <div className="modal-body">
          {showItems ? (
            <ProductItems
              productId={currentProduct.productid}
              productName={currentProduct.productname}
              onBack={() => setShowItems(false)}
              refreshTrigger={itemsRefreshTrigger}
            />
          ) : (
            <>
              <div className="image-section">
                {form.image_url ? (
                  <img src={form.image_url} alt="Product" className="product-image" />
                ) : (
                  <div className="img-placeholder" />
                )}
                <button
                  className="update-image-button"
                  onClick={handleImageClick}
                  disabled={!isEditing}
                >
                  Update Image
                </button>
                <input
                  type="file"
                  accept="image/*"
                  ref={fileInputRef}
                  style={{ display: "none" }}
                  onChange={handleImageChange}
                />
              </div>

              <div className="info-section">
                {formError && <div className="form-warning">{formError}</div>}

                <label>Product title</label>
                <input
                  type="text"
                  name="productname"
                  value={form.productname || ""}
                  onChange={handleChange}
                  disabled={!isEditing}
                  placeholder="Enter product name"
                />

                <label>Description</label>
                <input
                  type="text"
                  name="description"
                  value={form.description || ""}
                  onChange={handleChange}
                  disabled={!isEditing}
                  placeholder="Enter product description"
                />

                <label>Supplier</label>
                <div className="supplier-container">
                  {!isEditing ? (
                    <div className="supplier-display">
                      <span className="supplier-name">{getCurrentSupplierName()}</span>
                    </div>
                  ) : (
                    <div className="supplier-edit-container">
                      <select
                        name="supplierid"
                        value={form.supplierid || ""}
                        onChange={handleChange}
                        className="supplier-select"
                      >
                        <option value="">-- Select Supplier --</option>
                        {suppliers.map((s) => (
                          <option key={s.supplierid} value={s.supplierid}>
                            {s.suppliername}
                          </option>
                        ))}
                      </select>
                      {form.supplierid && (
                        <div className="selected-supplier-preview">
                          Selected: <strong>{getCurrentSupplierName()}</strong>
                        </div>
                      )}
                    </div>
                  )}
                </div>
              </div>
            </>
          )}
        </div>

        {isEditing && !showItems && (
          <div className="modal-footer">
            <button className="cancel-button" onClick={handleCancel} disabled={isLoading}>
              Cancel
            </button>
            <button className="save-button" onClick={handleSave} disabled={isLoading}>
              {isLoading ? "Saving..." : "Save"}
            </button>
          </div>
        )}

        <ConfirmDeleteModal
          isOpen={showConfirm}
          onConfirm={handleDelete}
          onCancel={() => {
            setShowConfirm(false);
            setDeleteError("");
          }}
          productName={currentProduct.productname}
        />
        {formError && <div className="productForm-warning">{formError}</div>}
        {deleteError && <div className="delete-warning">{deleteError}</div>}
          
        {showAddCategory && (
          <AddCategory
            productId={currentProduct.productid}
            onClose={() => setShowAddCategory(false)}
            onCategoryAdded={() => {
              setShowAddCategory(false);
              setItemsRefreshTrigger((prev) => prev + 1);
            }}
          />
        )}
      </div>
    </div>
  );
};

export default ViewProduct;
