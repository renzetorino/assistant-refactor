import React, { useEffect, useRef, useState } from 'react';
import { uploadAttachments, listAttachments, deleteAttachment } from '../api/attachments';
import '../stylecss/AttachmentPanel.css';

export function AttachmentsPanel({ expenseId, onClose }) {
  const [files, setFiles] = useState([]);
  const [rows, setRows] = useState([]);
  const [loading, setLoading] = useState(false);
  const fileInputRef = useRef(null);

  async function refresh() {
    const d = await listAttachments(expenseId);
    setRows(d || []);
  }
  useEffect(() => { refresh(); }, [expenseId]);

  async function onUpload() {
    if (!files.length) return;
    setLoading(true);
    try {
      await uploadAttachments(expenseId, files);
      setFiles([]);
      await refresh();
    } finally {
      setLoading(false);
    }
  }

  const prettySize = (n) =>
    typeof n !== 'number'
      ? ''
      : n < 1024
      ? `${n} B`
      : n < 1024 * 1024
      ? `${(n / 1024).toFixed(1)} KB`
      : `${(n / 1024 / 1024).toFixed(1)} MB`;

  const ext = (name = '') => name.split('.').pop()?.toUpperCase();

  function handlePickFiles() {
    fileInputRef.current?.click();
  }

  function handleFileChange(e) {
    setFiles(Array.from(e.target.files ?? []));
  }

  return (
    <section className="ap-card">
      {/* Sticky header with title + actions */}
      <header className="ap-header">
        <div className="ap-headings">
          <h3 className="ap-title">Attachments</h3>
          <p className="ap-subtitle">
            Upload receipts, invoices, or photos related to this expense.
          </p>
        </div>

        <div className="ap-actions">
          <button
            type="button"
            className="btn ghost"
            onClick={onClose}
            title="Close attachments"
          >
            Close
          </button>

          <input
            ref={fileInputRef}
            type="file"
            accept="image/*,application/pdf"
            multiple
            hidden
            onChange={handleFileChange}
          />

          <button
            type="button"
            className="btn primary"
            onClick={handlePickFiles}
            title="Choose files"
          >
            Upload
          </button>

          <button
            type="button"
            className="btn subtle"
            onClick={onUpload}
            disabled={loading || !files.length}
            title={files.length ? `Upload ${files.length} file(s)` : 'Choose files first'}
          >
            {loading ? 'Uploading…' : files.length ? `Upload ${files.length}` : 'Start upload'}
          </button>
        </div>
      </header>

      {/* Top info / stats row */}
      <div className="attachments-toolbar">
        <div className="pill">
          {rows.length} file{rows.length === 1 ? '' : 's'}
        </div>
        {files.length > 0 && (
          <div className="pill soft">
            Pending: {files.length} file{files.length === 1 ? '' : 's'}
          </div>
        )}
      </div>

      {/* Grid */}
      <div className="attachments-scroll">
        <div className="attachments-grid">
          {(loading ? Array.from({ length: 6 }) : rows).map((a, i) => {
            if (loading) return <div key={i} className="attachment-card skeleton" />;

            const url = a?.url;
            const name =
              a?.name ||
              (url ? decodeURIComponent(url.split('?')[0].split('/').pop() || 'file') : 'file');
            const sizeLabel = prettySize(a?.size_bytes);

            return (
              <article key={a.id || i} className="attachment-card" title={name}>
                <div className="attachment-thumb">
                  {url ? (
                    <img
                      src={url}
                      alt={name}
                      onError={(e) => {
                        const div = document.createElement('div');
                        div.className = 'file-fallback';
                        div.textContent = '📄';
                        e.currentTarget.replaceWith(div);
                      }}
                    />
                  ) : (
                    <div className="file-fallback">📄</div>
                  )}
                </div>

                <div className="attachment-meta">
                  <div className="attachment-name">{name}</div>

                  <div className="attachment-sub">
                    <span className="badge">{ext(name) || 'FILE'}</span>
                    {sizeLabel && <span>{sizeLabel}</span>}
                  </div>

                  <div className="attachment-actions">
                    {url ? (
                      <>
                        <a className="btn-xs" href={url} target="_blank" rel="noreferrer">
                          Open
                        </a>
                        <a className="btn-xs" href={url} download>
                          Download
                        </a>
                      </>
                    ) : (
                      <span className="btn-xs" style={{ opacity: 0.6, pointerEvents: 'none' }}>
                        No URL
                      </span>
                    )}

                    <button
                      className="btn-xs danger"
                      onClick={() => deleteAttachment(a.id).then(refresh)}
                    >
                      Delete
                    </button>
                  </div>
                </div>
              </article>
            );
          })}
        </div>
      </div>
    </section>
  );
}
