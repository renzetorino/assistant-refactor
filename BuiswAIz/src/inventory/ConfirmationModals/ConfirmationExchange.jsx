import React from "react";
import "../ConfirmationModals/Style/ExchangeConfirmationModal.css";

const ExchangeConfirmationModal = ({ message, onConfirm, onCancel, loading }) => {
  return (
    <div className="exchConfirm-overlay">
      <div className="exchConfirm-box">
        <h3>Confirm Exchange</h3>
        <p>{message}</p>

        <div className="exchConfirm-actions">
          <button
            className="exchCancel-btn"
            onClick={onCancel}
            disabled={loading}
          >
            Cancel
          </button>

          <button
            className="exchConfirm-btn"
            onClick={onConfirm}
            disabled={loading}
          >
            {loading ? "Processing..." : "Confirm"}
          </button>
        </div>
      </div>
    </div>
  );
};

export default ExchangeConfirmationModal;
