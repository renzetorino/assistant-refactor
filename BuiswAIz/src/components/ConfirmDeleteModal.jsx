// src/components/ConfirmDeleteModal.jsx
import React, { useState } from "react";
import "./style/ConfirmDeleteModal.css"; 

export default function ConfirmDeleteModal({
  isOpen,
  title = "Confirm Delete",
  name,            // optional: the entity name to show in <b>…</b>
  message,         // optional: full custom message; overrides name if provided
  confirmLabel = "Delete",
  cancelLabel = "Cancel",
  onConfirm,
  onCancel,
}) {
  const [loading, setLoading] = useState(false);
  if (!isOpen) return null;

  const handleConfirm = async () => {
    setLoading(true);
    try { await onConfirm?.(); }
    finally { setLoading(false); }
  };

  return (
    <div className="supplyConfirm-overlay">
      <div className="supplyConfirm-box">
        <h3>{title}</h3>
        <p>
          {message
            ? message
            : <>Are you sure you want to delete <b>{name ?? "this item"}</b>? This action cannot be undone.</>}
        </p>

        <div className="supplyConfirm-actions">
          <button className="supplyAbort-btn" onClick={onCancel} disabled={loading}>
            {cancelLabel}
          </button>
          <button className="supplyConfirm-button" onClick={handleConfirm} disabled={loading}>
            {loading ? "Deleting…" : confirmLabel}
          </button>
        </div>
      </div>
    </div>
  );
}
