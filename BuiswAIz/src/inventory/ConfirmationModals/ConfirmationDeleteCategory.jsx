// ConfirmDeleteModalCategory.jsx
import React from "react";
import "../ConfirmationModals/Style/ConfirmDeleteModal.css";

const ConfirmDeleteModalCategory = ({show, onClose, onConfirm, message, disableConfirm,}) => {
  if (!show) return null;

  return (
    <div className="modal-overlay">
      <div className="modal-content">
        <p>{message}</p>
        <div className="modal-actions">
          <button onClick={onClose} className="cancel-button">
            Cancel
          </button>
          <button
            onClick={onConfirm}
            className="confirm-button"
            disabled={disableConfirm} // 🚫 disable when deletion blocked
          >
            {disableConfirm ? "Not Allowed" : "Delete"} {/* ✅ dynamic label */}
          </button>
        </div>
      </div>
    </div>
  );
};

export default ConfirmDeleteModalCategory;
