// src/components/ConfirmActionModal.jsx
import React, { useState } from "react";
import "./style/ConfirmDeleteModal.css"; // reuse same styles

export default function ConfirmActionModal({
  isOpen,
  title = "Are you sure?",
  message,
  confirmLabel = "Confirm",
  cancelLabel = "Cancel",
  tone = "warning", // "warning" | "danger" | "default"
  onCancel,
  onConfirm,
}) {
  const [loading, setLoading] = useState(false);
  if (!isOpen) return null;

  const handleConfirm = async () => {
    setLoading(true);
    try { await onConfirm?.(); }
    finally { setLoading(false); }
  };

  return (
    <div className="supplyConfirm-overlay" role="dialog" aria-modal="true"
         onMouseDown={(e)=>{ if (e.target === e.currentTarget) onCancel?.(); }}>
      <div className={`supplyConfirm-box tone--${tone}`}>
        <h3>{title}</h3>
        <p style={{ whiteSpace:'pre-wrap' }}>{message}</p>
        <div className="supplyConfirm-actions">
          <button className="supplyAbort-btn" onClick={onCancel} disabled={loading}>
            {cancelLabel}
          </button>
          <button className="supplyConfirm-button" onClick={handleConfirm} disabled={loading}>
            {loading ? "Working…" : confirmLabel}
          </button>
        </div>
      </div>
    </div>
  );
}
