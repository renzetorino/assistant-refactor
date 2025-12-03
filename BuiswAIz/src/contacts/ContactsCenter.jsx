// src/contacts/ContactsCenter.jsx
import React, { useEffect, useMemo, useRef, useState } from "react";
import { supabase } from "../supabase";
import ConfirmDeleteModal from "../components/ConfirmDeleteModal";
import "./contacts.css";

/* ------------ tiny toast (no deps) ------------ */
function useToasts() {
  const [toasts, setToasts] = useState([]);
  const idRef = useRef(1);
  function push(message, type = "success", ttl = 3000) {
    const id = idRef.current++;
    setToasts((t) => [...t, { id, message, type }]);
    setTimeout(() => setToasts((t) => t.filter((x) => x.id !== id)), ttl);
  }
  return [
    toasts,
    {
      success: (m, ttl) => push(m, "success", ttl),
      error: (m, ttl) => push(m, "error", ttl),
      info: (m, ttl) => push(m, "info", ttl),
    },
  ];
}

/* ------------ helpers ------------ */
const fmtDate = (iso) => (iso ? new Date(iso).toLocaleDateString() : "—");
const peso = (n) =>
  `₱${Number(n || 0).toLocaleString(undefined, {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })}`;

/**
 * Props:
 *  - triggerLabel?: string
 *  - triggerClass?: string
 *  - onOpenExpense?: (expenseRowOrId) => void   // used to open parent edit modal
 */
export default function ContactsCenter({
  triggerLabel = "Contacts",
  triggerClass = "btn outline",
  onOpenExpense, // optional callback to open the parent's Edit Expense modal
}) {
  const [open, setOpen] = useState(false);

  const [loading, setLoading] = useState(false);
  const [rows, setRows] = useState([]);
  const [err, setErr] = useState("");

  const [search, setSearch] = useState("");

  const [addOpen, setAddOpen] = useState(false);
  const [name, setName] = useState("");
  const [email, setEmail] = useState("");
  const [phone, setPhone] = useState("");

  const [editOpen, setEditOpen] = useState(false);
  const [editing, setEditing] = useState(null);
  const [editName, setEditName] = useState("");
  const [editEmail, setEditEmail] = useState("");
  const [editPhone, setEditPhone] = useState("");

  const [active, setActive] = useState(null);
  const [tab, setTab] = useState("details");
  const [records, setRecords] = useState([]);
  const [recordsLoading, setRecordsLoading] = useState(false);

  const [confirmOpen, setConfirmOpen] = useState(false);
  const confirmRef = useRef({}); // { name, onConfirm }

  const [toasts, toast] = useToasts();

  async function refresh() {
    setLoading(true);
    setErr("");
    try {
      const { data, error } = await supabase
        .from("contacts")
        .select("id, name, email, phone, created_at, updated_at")
        .order("name", { ascending: true });
      if (error) throw error;
      setRows(data || []);
      if (active) {
        const found = (data || []).find((x) => x.id === active.id);
        setActive(found || null);
      }
    } catch (e) {
      setErr(e.message || "Failed to load contacts");
      toast.error("Failed to load contacts");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    if (open) refresh();
  }, [open]);

  // Filtered contacts (search by name/email/phone)
  const filteredRows = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return rows;
    return rows.filter((r) => {
      const hay = `${r.name || ""} ${r.email || ""} ${r.phone || ""}`.toLowerCase();
      return hay.includes(q);
    });
  }, [rows, search]);

  // Load expense records for selected contact
  async function loadRecords(contact) {
    if (!contact) return;
    setRecordsLoading(true);
    try {
      let { data, error } = await supabase
        .from("expenses")
        .select("id, occurred_on, amount, notes, status, contact_id, category_id")
        .eq("contact_id", contact.id)
        .order("occurred_on", { ascending: false });

      if (error) {
        // Fallback (very rare): do it without order, then sort locally
        const fallback = await supabase
          .from("expenses")
          .select("id, occurred_on, amount, notes, status, contact_id, category_id")
          .eq("contact_id", contact.id);

        if (fallback.error) throw fallback.error;

        data = (fallback.data || []).sort(
          (a, b) => new Date(b.occurred_on) - new Date(a.occurred_on)
        );
      }
      setRecords(data || []);
    } catch (e) {
      toast.error(e.message || "Failed to load records");
    } finally {
      setRecordsLoading(false);
    }
  }

  useEffect(() => {
    if (active && tab === "records") loadRecords(active);
  }, [active, tab]);

  async function onAdd(e) {
    e.preventDefault();
    try {
      const { data, error } = await supabase
        .from("contacts")
        .insert({
          name: name.trim(),
          email: email.trim() || null,
          phone: phone.trim() || null,
        })
        .select("id, name, email, phone, created_at, updated_at")
        .single();
      if (error) throw error;
      setAddOpen(false);
      setName("");
      setEmail("");
      setPhone("");
      toast.success("Contact added");
      await refresh();
      setActive(data);
      setTab("details");
    } catch (e2) {
      toast.error(e2.message || "Failed to add contact");
    }
  }

  function openEdit(row) {
    setEditing(row);
    setEditOpen(true);
    setEditName(row.name || "");
    setEditEmail(row.email || "");
    setEditPhone(row.phone || "");
  }

  async function onEditSubmit(e) {
    e.preventDefault();
    if (!editing) return;
    try {
      const { data, error } = await supabase
        .from("contacts")
        .update({
          name: editName.trim(),
          email: editEmail.trim() || null,
          phone: editPhone.trim() || null,
        })
        .eq("id", editing.id)
        .select("id, name, email, phone, created_at, updated_at")
        .single();
      if (error) throw error;
      setEditOpen(false);
      setEditing(null);
      toast.success("Contact updated");
      await refresh();
      setActive(data);
    } catch (e2) {
      toast.error(e2.message || "Failed to update contact");
    }
  }

  function onDelete(row) {
    setConfirmOpen(true);
    confirmRef.current = {
      name: row.name,
      onConfirm: async () => {
        setConfirmOpen(false);
        try {
          // (optional) show how many will be affected
          const { count } = await supabase
            .from("expenses")
            .select("id", { count: "exact", head: true })
            .eq("contact_id", row.id);

          // 1) Unlink expenses first (avoid FK conflict)
          const upd = await supabase
            .from("expenses")
            .update({ contact_id: null })
            .eq("contact_id", row.id);
          if (upd.error) throw upd.error;

          // 2) Now delete the contact
          const del = await supabase.from("contacts").delete().eq("id", row.id);
          if (del.error) throw del.error;

          // 3) Update UI
          setRows((prev) => prev.filter((r) => r.id !== row.id));
          toast.success(
            count ? `Deleted contact. Unlinked ${count} expense(s).` : "Deleted contact."
          );
        } catch (e) {
          toast.error(e?.message || "Failed to delete contact");
        }
      },
    };
  }

  const totals = useMemo(() => {
    const sum = records.reduce((s, r) => s + Number(r.amount || 0), 0);
    return { count: records.length, sum };
  }, [records]);

  function downloadCSV() {
    if (!active) return;
    const header = ["Date", "Notes", "Amount", "Status"];
    const lines = [header.join(",")];
    for (const r of records) {
      const row = [
        fmtDate(r.occurred_on),
        (r.notes || "").replaceAll('"', '""'),
        Number(r.amount || 0).toFixed(2),
        r.status || "",
      ];
      lines.push(row.map((v) => `"${String(v)}"`).join(","));
    }
    const csv = lines.join("\n");
    const blob = new Blob([csv], { type: "text/csv;charset=utf-8;" });
    const url = URL.createObjectURL(blob);
    const nameSafe = (active.name || "contact").replace(/[^a-z0-9-_]+/gi, "_");
    const a = document.createElement("a");
    a.href = url;
    a.download = `${nameSafe}_records_${new Date().toISOString().slice(0, 10)}.csv`;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
  }

  // Click a record row: fetch freshest row (if needed) then call parent opener
  async function openRecordInParent(r) {
    if (!onOpenExpense) return;
    try {
      const { data, error } = await supabase
        .from("expenses")
        .select("id, occurred_on, amount, notes, status, contact_id, category_id")
        .eq("id", r.id)
        .maybeSingle();
      if (error) throw error;
      onOpenExpense(data || r);
      // optional: close this modal after handing off
      setOpen(false);
    } catch (e) {
      console.error(e);
      onOpenExpense(r.id); // at least pass the id
    }
  }

  return (
    <>
      {/* Trigger */}
      <button className={triggerClass} onClick={() => setOpen(true)}>
        {triggerLabel}
      </button>

      {!open ? null : (
        <div
          className="modal-overlay"
          onMouseDown={(e) => {
            if (e.target === e.currentTarget) setOpen(false);
          }}
          style={{ zIndex: 1100 }}
        >
          <div
            className="modal"
            onMouseDown={(e) => e.stopPropagation()}
            style={{
              width: "min(980px, 94vw)",
              maxHeight: "90vh",
              overflow: "auto",
              position: "relative",
              zIndex: 1110,
            }}
          >
            <div
              className="header-bar"
              style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}
            >
              <h2 style={{ margin: 0 }}>Contacts</h2>
              <button
                type="button"
                className="btn icon"
                aria-label="Close"
                onMouseDown={(e) => e.stopPropagation()}
                onClick={(e) => {
                  e.stopPropagation();
                  setOpen(false);
                }}
              >
                ✕
              </button>
            </div>

            {/* Search + Add */}
            <div
              className="contacts-toolbar"
              style={{
                display: "flex",
                gap: 12,
                alignItems: "center",
                justifyContent: "space-between",
                margin: "8px 0 12px",
                flexWrap: "wrap",
              }}
            >
              <div style={{ display: "flex", gap: 8, alignItems: "center" }}>
                <input
                  className="contact-search"
                  placeholder="Search contacts…"
                  value={search}
                  onChange={(e) => setSearch(e.target.value)}
                />
                {search && (
                  <button className="btn xs" onClick={() => setSearch("")} title="Clear">
                    Clear
                  </button>
                )}
              </div>

              {/* Right: Only the trigger now */}
              <div>
                <button className="btn primary" onClick={() => setAddOpen(true)}>
                  + Add contact
                </button>
              </div>
            </div>

            {/* Contacts table */}
            <div className="history-wrap" style={{ marginBottom: 16 }}>
              {loading ? (
                <p>Loading contacts…</p>
              ) : (
                <div className="logs-scroll">
                  <table>
                    <thead>
                      <tr>
                        <th style={{ textAlign: "left" }}>Name</th>
                        <th style={{ textAlign: "left" }}>Email</th>
                        <th style={{ textAlign: "left" }}>Phone</th>
                        <th style={{ textAlign: "right" }}>Actions</th>
                      </tr>
                    </thead>
                    <tbody>
                      {filteredRows.map((r) => (
                        <tr
                          key={r.id}
                          style={{ cursor: "pointer" }}
                          onClick={() => {
                            setActive(r);
                            setTab("details");
                          }}
                          title="Open contact"
                        >
                          <td>{r.name}</td>
                          <td>{r.email || "—"}</td>
                          <td>{r.phone || "—"}</td>
                          <td style={{ textAlign: "right" }}>
                            <button
                              className="btn xs"
                              onClick={(e) => {
                                e.stopPropagation();
                                openEdit(r);
                              }}
                            >
                              Edit
                            </button>
                            <button
                              className="btn xs danger"
                              style={{ marginLeft: 8 }}
                              onClick={(e) => {
                                e.stopPropagation();
                                onDelete(r);
                              }}
                            >
                              Delete
                            </button>
                          </td>
                        </tr>
                      ))}
                      {!filteredRows.length && (
                        <tr>
                          <td colSpan="4" className="muted">
                            {rows.length ? "No matches." : "No contacts yet. Add one above."}
                          </td>
                        </tr>
                      )}
                    </tbody>
                  </table>
                </div>
              )}
            </div>

            {/* Detail panel */}
            {active && (
              <div className="history-wrap">
                <div
                  style={{
                    display: "flex",
                    justifyContent: "space-between",
                    alignItems: "baseline",
                    gap: 8,
                  }}
                >
                  <h3 style={{ margin: 0 }}>{active.name}</h3>
                  <div className="muted">
                    Created: <strong>{fmtDate(active.created_at)}</strong>
                  </div>
                </div>

                <div className="tabs" style={{ marginTop: 10 }}>
                  <button
                    className={tab === "details" ? "active" : ""}
                    onClick={() => setTab("details")}
                  >
                    Details
                  </button>
                  <button
                    className={tab === "records" ? "active" : ""}
                    onClick={() => setTab("records")}
                  >
                    Records
                  </button>
                </div>

                <div className="tab-body" style={{ marginTop: 12 }}>
                  {tab === "details" && (
                    <div className="contact-details-card">
                      <div className="kv">
                        <div className="k">Name</div>
                        <div className="v">{active.name}</div>
                      </div>
                      <div className="kv">
                        <div className="k">Email</div>
                        <div className="v">{active.email || "—"}</div>
                      </div>
                      <div className="kv">
                        <div className="k">Phone</div>
                        <div className="v">{active.phone || "—"}</div>
                      </div>
                      <div className="kv">
                        <div className="k">Created</div>
                        <div className="v">{fmtDate(active.created_at)}</div>
                      </div>
                      <div className="kv">
                        <div className="k">Updated</div>
                        <div className="v">{fmtDate(active.updated_at)}</div>
                      </div>
                    </div>
                  )}

                  {tab === "records" && (
                    <div className="table-wrap">
                      {recordsLoading ? (
                        <p>Loading records…</p>
                      ) : (
                        <>
                          <div
                            className="records-summary muted"
                            style={{ display: "flex", gap: 16, alignItems: "center" }}
                          >
                            <span>
                              Total records: <strong>{totals.count}</strong>
                            </span>
                            <span>
                              Sum: <strong>{peso(totals.sum)}</strong>
                            </span>
                            <div style={{ marginLeft: "auto" }}>
                              <button className="btn xs" onClick={downloadCSV}>
                                Export CSV
                              </button>
                            </div>
                          </div>

                          <div className="logs-scroll">
                            <table>
                              <thead>
                                <tr>
                                  <th style={{ textAlign: "left" }}>Date</th>
                                  <th style={{ textAlign: "left" }}>Notes</th>
                                  <th style={{ textAlign: "right" }}>Amount</th>
                                  <th style={{ textAlign: "left" }}>Status</th>
                                </tr>
                              </thead>
                              <tbody>
                                {records.map((r) => (
                                  <tr
                                    key={r.id}
                                    className="clickable-row"
                                    style={{ cursor: onOpenExpense ? "pointer" : "default" }}
                                    onClick={() => onOpenExpense && openRecordInParent(r)}
                                    title={onOpenExpense ? "Open in editor" : ""}
                                  >
                                    <td>{fmtDate(r.occurred_on)}</td>
                                    <td>{r.notes || ""}</td>
                                    <td style={{ textAlign: "right" }}>{peso(r.amount)}</td>
                                    <td className="capitalize">{r.status || "—"}</td>
                                  </tr>
                                ))}
                                {!records.length && (
                                  <tr>
                                    <td colSpan="4" className="muted">
                                      No expenses linked to this contact.
                                    </td>
                                  </tr>
                                )}
                              </tbody>
                            </table>
                          </div>
                        </>
                      )}
                    </div>
                  )}
                </div>
              </div>
            )}
          </div>
        </div>
      )}

      {/* Add Contact Modal */}
      {addOpen && (
        <div
          className="modal-overlay"
          onMouseDown={(e) => {
            if (e.target === e.currentTarget) setAddOpen(false);
          }}
          style={{ zIndex: 1100 }}
        >
          <form
            className="modal"
            onMouseDown={(e) => e.stopPropagation()}
            onSubmit={onAdd}
            style={{ position: "relative", zIndex: 1110 }}
          >
            <div
              className="modal-header"
              style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}
            >
              <h3 style={{ margin: 0 }}>Add Contact</h3>
              <button
                type="button"
                className="btn icon"
                onMouseDown={(e) => e.stopPropagation()}
                onClick={(e) => {
                  e.stopPropagation();
                  setAddOpen(false);
                }}
                aria-label="Close"
              >
                ✕
              </button>
            </div>

            <div className="modal-body contact-edit-grid">
              <label className="stack">
                <span className="muted">Name</span>
                <input
                  className="input"
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                  required
                  placeholder="Juan Dela Cruz"
                />
              </label>
              <label className="stack">
                <span className="muted">Email</span>
                <input
                  className="input"
                  type="email"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  placeholder="juan@example.com"
                />
              </label>
              <label className="stack">
                <span className="muted">Phone</span>
                <input
                  className="input"
                  value={phone}
                  onChange={(e) => setPhone(e.target.value)}
                  placeholder="+63…"
                />
              </label>
            </div>

            <div className="modal-footer" style={{ display: "flex", justifyContent: "flex-end", gap: 8 }}>
              <button
                type="button"
                className="btn outline"
                onMouseDown={(e) => e.stopPropagation()}
                onClick={(e) => {
                  e.stopPropagation();
                  setAddOpen(false);
                  setName("");
                  setEmail("");
                  setPhone("");
                }}
              >
                Cancel
              </button>
              <button type="submit" className="btn primary">
                Save
              </button>
            </div>
          </form>
        </div>
      )}

      {/* Edit Modal */}
      {editOpen && (
        <div
          className="modal-overlay"
          onMouseDown={(e) => {
            if (e.target === e.currentTarget) setEditOpen(false);
          }}
          style={{ zIndex: 1100 }}
        >
          <form
            className="modal"
            onMouseDown={(e) => e.stopPropagation()}
            onSubmit={onEditSubmit}
            style={{ position: "relative", zIndex: 1110 }}
          >
            <div
              className="modal-header"
              style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}
            >
              <h3 style={{ margin: 0 }}>Edit Contact</h3>
              <button
                type="button"
                className="btn icon"
                aria-label="Close"
                onMouseDown={(e) => e.stopPropagation()}
                onClick={(e) => {
                  e.stopPropagation();
                  setEditOpen(false); // <-- fix (was setOpen(false))
                }}
              >
                ✕
              </button>
            </div>
            <div className="modal-body contact-edit-grid">
              <label className="stack">
                <span className="muted">Name</span>
                <input
                  className="input"
                  value={editName}
                  onChange={(e) => setEditName(e.target.value)}
                  required
                />
              </label>
              <label className="stack">
                <span className="muted">Email</span>
                <input
                  className="input"
                  type="email"
                  value={editEmail}
                  onChange={(e) => setEditEmail(e.target.value)}
                />
              </label>
              <label className="stack">
                <span className="muted">Phone</span>
                <input
                  className="input"
                  value={editPhone}
                  onChange={(e) => setEditPhone(e.target.value)}
                />
              </label>
            </div>
            <div className="modal-footer" style={{ display: "flex", justifyContent: "flex-end", gap: 8 }}>
              <button
                type="button"
                className="btn outline"
                onMouseDown={(e) => e.stopPropagation()}
                onClick={(e) => {
                  e.stopPropagation();
                  setEditOpen(false);
                }}
              >
                Cancel
              </button>
              <button type="submit" className="btn primary">
                Save changes
              </button>
            </div>
          </form>
        </div>
      )}

      <ConfirmDeleteModal
        isOpen={confirmOpen}
        name={confirmRef.current.name}
        onCancel={() => setConfirmOpen(false)}
        onConfirm={confirmRef.current.onConfirm}
      />

      {/* Toasts */}
      <div className="toast-stack">
        {toasts.map((t) => (
          <div key={t.id} className={`toast ${t.type}`}>
            {t.message}
          </div>
        ))}
      </div>
    </>
  );
}
