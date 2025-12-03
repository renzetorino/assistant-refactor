import React, { useState } from "react";
import "../stylecss/ViewSupplier.css";
import { supabase } from "../supabase";
import SupplierDeleteModal from "./ConfirmationModal/SupplierConfirmation";

const ViewSupplier = ({ supplier, onClose, onSupplierUpdated }) => {
  const [isEditing, setIsEditing] = useState(false);
  const [form, setForm] = useState({ ...supplier });
  const [isSaving, setIsSaving] = useState(false);
  const [deleteError, setDeleteError] = useState("");
  const [showConfirm, setShowConfirm] = useState(false);
  const handleChange = (e) => {
    const { name, value } = e.target;
    setForm((prev) => ({ ...prev, [name]: value }));
  };

  const handleUpdate = () => setIsEditing(true);

  const handleSave = async () => {
    setIsSaving(true);
    try {
      const { error } = await supabase
        .from("suppliers")
        .update({
          suppliername: form.suppliername,
          contactperson: form.contactperson,
          supplieremail: form.supplieremail,
          // ✅ Remove dashes before saving
          phonenumber: form.phonenumber.replace(/-/g, ""),
          address: form.address,
          supplierstatus: form.supplierstatus,
        })
        .eq("supplierid", supplier.supplierid);

      if (error) throw error;

      setIsEditing(false);
      if (onSupplierUpdated) onSupplierUpdated();
    } catch (err) {
      console.error("Error updating supplier:", err.message);
    } finally {
      setIsSaving(false);
    }
  };


  const handleDelete = async () => {
    setDeleteError(""); // reset previous error
    try {
      const { data: productData, error: productError } = await supabase
        .from("products")
        .select("productid")
        .eq("supplierid", supplier.supplierid)
        .limit(1);

      if (productError) throw productError;

      if (productData?.length > 0) {
        setDeleteError("This supplier cannot be deleted because a product is referenced to it");
        return;
      }

      // If no associated products, delete the supplier
      const { error } = await supabase
        .from("suppliers")
        .delete()
        .eq("supplierid", supplier.supplierid);

      if (error) throw error;

      if (onSupplierUpdated) onSupplierUpdated();
      onClose();
    } catch (err) {
      console.error("Error deleting supplier:", err.message);
      setDeleteError("Error deleting supplier. Check console for details.");
    }
  };


  return (
    <div className="view-supplier-overlay">
      <div className="view-supplier-modal">
        <div className="modal-header">
          <button onClick={onClose} className="back-button">←</button>
          <h2>Supplier</h2>
          <span className="supplier-id-tag">#{supplier.supplierid}</span>
          <div className="header-actions">
            <button className="delete-button" onClick={() => setShowConfirm(true)}>🗑 Delete</button>
            <button className="update-button" onClick={handleUpdate} disabled={isEditing}>Update</button>
          </div>
        </div>

        <div className="modal-body">
          <label>Supplier Name</label>
          <input
            type="text"
            name="suppliername"
            value={form.suppliername}
            onChange={handleChange}
            disabled={!isEditing}
          />

          <label>Contact Person</label>
          <input
            type="text"
            name="contactperson"
            value={form.contactperson}
            onChange={handleChange}
            disabled={!isEditing}
          />

          <label>Email</label>
          <input
            type="email"
            name="supplieremail"
            value={form.supplieremail}
            onChange={handleChange}
            disabled={!isEditing}
          />

          <label>Phone Number</label>
          <input
            type="tel"
            name="phonenumber"
            value={form.phonenumber}
            onChange={(e) => {
              if (!isEditing) return;

              let value = e.target.value.replace(/\D/g, ""); // keep only digits

              // ✅ Format for PH mobile numbers (11 digits): 0927-538-7129
              if (value.length > 4 && value.length <= 7) {
                value = `${value.slice(0, 4)}-${value.slice(4)}`;
              } else if (value.length > 7) {
                value = `${value.slice(0, 4)}-${value.slice(4, 7)}-${value.slice(7, 11)}`;
              }

              setForm((prev) => ({ ...prev, phonenumber: value }));
            }}
            disabled={!isEditing}
          />

          <label>Address</label>
          <input
            type="text"
            name="address"
            value={form.address}
            onChange={handleChange}
            disabled={!isEditing}
          />

          <label>Status</label>
          <select
            name="supplierstatus"
            value={form.supplierstatus}
            onChange={handleChange}
            disabled={!isEditing}
          >
            <option value="Active">Active</option>
            <option value="Inactive">Inactive</option>
          </select>
        </div>

        {isEditing && (
          <div className="modal-footer">
            <button className="save-button" onClick={handleSave}>
              {isSaving ? "Saving..." : "Save"}
            </button>
          </div>
        )}

        {deleteError && 
          <p className="supplyDelete-warning">
            {deleteError}
          </p>
        }
        
         
      </div>
      <SupplierDeleteModal
            isOpen={showConfirm}
            onConfirm={async () => {
              await handleDelete(); 
              setShowConfirm(false); 
          }}
          onCancel={() => setShowConfirm(false)}
          suppliername={supplier.suppliername}
        />
    </div>
  );
};

export default ViewSupplier;
