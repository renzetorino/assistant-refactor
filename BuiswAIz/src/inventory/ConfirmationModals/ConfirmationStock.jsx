import React, { useState } from "react";
import "../ConfirmationModals/Style/AddStockConfirmation.css";

const ConfirmStockModal = ({ isOpen, onConfirm, onCancel, productName }) => {
  const [loading, setLoading] = useState(false);

  if (!isOpen) return null;

  const AddHandleSubmit = async () =>{
    setLoading(true);
    try {    
        await onConfirm();
    }   finally{
        setLoading(false);
    }
  };
    


  return (
    <div className="confirmStock-overlay">
      <div className="confirmStock-box">
        <h3>Confirm Add stock</h3>
        <p>
          Are you sure you want to restock <b>{productName}</b>? This action cannot be undone.
        </p>
        <div className="confirmStock-actions">
          <button className="stockAbort-btn" onClick={onCancel} disabled={loading}>
            Cancel
          </button>
          <button
            className="stockConfirm-button"
            onClick={AddHandleSubmit}
            disabled={loading}
          >
            {loading ? <div className="spinner"></div> : "Add"}
          </button>
        </div>
      </div>
    </div>
  );
};

export default ConfirmStockModal;
