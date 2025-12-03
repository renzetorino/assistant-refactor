import React, { useState } from "react";
import "../ConfirmationModals/Style/DeleteConfirmation.css";

const ConfirmDeleteModal = ({ isOpen, onConfirm, onCancel, productName }) => {
  const [loading, setLoading] = useState(false); // <-- define loading state

  if (!isOpen) return null;

  const handleDelete = async () => {
    setLoading(true);
    try {
      await onConfirm(); // wait for delete to finish
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="confirm-overlay">
      <div className="confirm-box">
        <h3>Confirm Delete</h3>
        <p>
          Are you sure you want to delete <b>{productName}</b>? This action cannot be undone.
        </p>
        <div className="confirm-actions">
          <button className="abort-btn" onClick={onCancel} disabled={loading}>
            Cancel
          </button>
          <button
            className="confirm-button"
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

export default ConfirmDeleteModal;
