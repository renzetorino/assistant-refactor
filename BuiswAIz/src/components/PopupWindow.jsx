import React, { useEffect } from "react";

export default function PopupWindow({ open, title = "", onClose, children }) {
  useEffect(() => {
    if (!open) return;
    const onEsc = (e) => e.key === "Escape" && onClose?.();
    document.addEventListener("keydown", onEsc);
    document.body.style.overflow = "hidden";
    return () => {
      document.removeEventListener("keydown", onEsc);
      document.body.style.overflow = "";
    };
  }, [open, onClose]);

  if (!open) return null;

  return (
    <div className="pw-overlay" onClick={onClose}>
      <div className="pw-window" onClick={(e) => e.stopPropagation()}>
        <div className="pw-header">
          <div className="pw-title">{title || "AI Report"}</div>
          <button className="pw-close" aria-label="Close" onClick={onClose}>×</button>
        </div>
        <div className="pw-body">
          {children}
        </div>
      </div>
    </div>
  );
}
