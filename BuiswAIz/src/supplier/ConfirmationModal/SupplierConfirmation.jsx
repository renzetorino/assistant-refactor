import React, { useState } from "react";
import "../ConfirmationModal/style/SupplierConfirmation.css";

const SupplierDeleteModal = ({ isOpen, onConfirm, onCancel, suppliername }) => {
  const [loading, setLoading] = useState(false); 

  if (!isOpen) return null;

  const handleDelete = async () => {
    setLoading(true);
    try {
      await onConfirm();
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="supplyConfirm-overlay">
      <div className="supplyConfirm-box">
        <h3>Confirm Delete</h3>
        <p>
          Are you sure you want to delete <b>{suppliername}</b>? This action cannot be undone.
        </p>
        <div className="supplyConfirm-actions">
          <button className="supplyAbort-btn" onClick={onCancel} disabled={loading}>
            Cancel
          </button>
          <button
            className="supplyConfirm-button"
            onClick={handleDelete}
            disabled={loading}
          >
            {loading ? <div className="spinner"></div> : "Delete"}
          </button>
        </div>
      </div>
    </div>
  );
};

export default SupplierDeleteModal;
