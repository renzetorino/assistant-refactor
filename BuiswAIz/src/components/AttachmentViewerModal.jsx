// src/components/AttachmentViewerModal.jsx
import React, { useEffect, useMemo, useRef, useState } from "react";
import "./style/AttachmentViewerModal.css";

function isImage(url = "") {
  return /\.(png|jpe?g|gif|webp|bmp|svg)(\?.*)?$/i.test(url);
}
function isPdf(url = "") {
  return /\.pdf(\?.*)?$/i.test(url);
}

export default function AttachmentViewerModal({
  isOpen,
  items = [],          // [{ url, name }]
  startIndex = 0,
  onClose,
}) {
  const [index, setIndex] = useState(startIndex);
  const [zoom, setZoom] = useState(1);
  const [rotate, setRotate] = useState(0);
  const containerRef = useRef(null);

  const current = items[index] ?? null;

  useEffect(() => {
    if (!isOpen) return;
    const onKey = (e) => {
      if (e.key === "Escape") onClose?.();
      if (e.key === "ArrowRight") next();
      if (e.key === "ArrowLeft") prev();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [isOpen, index]);

  useEffect(() => {
    // reset transforms when switching files
    setZoom(1);
    setRotate(0);
  }, [index]);

  function next() {
    setIndex((i) => (i + 1) % items.length);
  }
  function prev() {
    setIndex((i) => (i - 1 + items.length) % items.length);
  }

  if (!isOpen || !current) return null;

  const viewer = isImage(current.url) ? (
    <img
      src={current.url}
      alt={current.name || "Attachment"}
      className="avm-media"
      style={{ transform: `scale(${zoom}) rotate(${rotate}deg)` }}
      draggable={false}
    />
  ) : isPdf(current.url) ? (
    <iframe
      src={current.url}
      title={current.name || "PDF"}
      className="avm-pdf"
    />
  ) : (
    <div className="avm-generic">
      <div className="avm-file-icon">📄</div>
      <div className="avm-file-name">{current.name || current.url}</div>
      <a className="avm-download" href={current.url} download>Download</a>
    </div>
  );

  return (
    <div
      className="avm-overlay"
      onMouseDown={(e) => { if (e.target === e.currentTarget) onClose?.(); }}
    >
      <div className="avm-modal" ref={containerRef}>
        {/* Header */}
        <div className="avm-header">
          <div className="avm-title" title={current.name || current.url}>
            {current.name || current.url}
          </div>
          <button className="avm-icon" onClick={onClose} aria-label="Close">✕</button>
        </div>

        {/* Viewer */}
        <div className="avm-body">
          {viewer}
        </div>

        {/* Toolbar */}
        <div className="avm-toolbar">
          <div className="avm-left">
            <button className="avm-btn" onClick={prev} disabled={items.length <= 1} title="Previous">←</button>
            <button className="avm-btn" onClick={next} disabled={items.length <= 1} title="Next">→</button>
          </div>
          <div className="avm-center">
            <button className="avm-btn" onClick={() => setZoom((z) => Math.max(0.25, z - 0.25))} title="Zoom out">−</button>
            <span className="avm-zoom">{Math.round(zoom * 100)}%</span>
            <button className="avm-btn" onClick={() => setZoom((z) => Math.min(5, z + 0.25))} title="Zoom in">+</button>
            <button className="avm-btn" onClick={() => setRotate((r) => (r + 90) % 360)} title="Rotate 90°">⤾</button>
            {current?.url && (
              <a className="avm-btn" href={current.url} download title="Download">⤓</a>
            )}
          </div>
          <div className="avm-right">
            <span className="avm-count">{index + 1} / {items.length}</span>
          </div>
        </div>

        {/* Thumbnails */}
        {items.length > 1 && (
          <div className="avm-thumbs">
            {items.map((it, i) => (
              <button
                key={it.url + i}
                className={`avm-thumb ${i === index ? "is-active" : ""}`}
                onClick={() => setIndex(i)}
                title={it.name || it.url}
              >
                {isImage(it.url)
                  ? <img src={it.url} alt={it.name || "thumb"} />
                  : isPdf(it.url) ? <span className="avm-thumb-badge">PDF</span>
                  : <span className="avm-thumb-badge">FILE</span>}
              </button>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
