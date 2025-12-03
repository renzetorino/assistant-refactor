// ──────────────────────────────────────────────
// Imports
// ──────────────────────────────────────────────
import React, { useEffect, useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import {
  format,
  isSameDay,
  isAfter,
  isBefore,
  startOfDay,
  endOfWeek,
  startOfMonth,
  endOfMonth,
  addMonths,
  addDays,
  addYears,
  parseISO,
  differenceInCalendarDays,
} from "date-fns";
import { supabase } from "../supabase";
import "../expenses/ExpenseDashboard.css";            // reuse your expense page layout utilities
import "../stylecss/PlannedPayments.css";   // small component-specific styles



// ──────────────────────────────────────────────
// Inline Calendar for Planned Payments
// ──────────────────────────────────────────────
  function PlannedCalendarInline({ payments, onSelectDate }) {
    const [viewMonth, setViewMonth] = React.useState(startOfMonth(new Date()));

    const monthStart = startOfMonth(viewMonth);
    const monthEnd   = endOfMonth(viewMonth);

    // Build grid days incl. leading blanks so the month starts on the right weekday
    const days = React.useMemo(() => {
      const firstWeekday = monthStart.getDay(); // 0=Sun..6=Sat
      const totalDays = differenceInCalendarDays(monthEnd, monthStart) + 1;

      const grid = [];
      // leading blanks
      for (let i = 0; i < firstWeekday; i++) grid.push(null);
      // month days
      for (let i = 0; i < totalDays; i++) grid.push(addDays(monthStart, i));
      return grid;
    }, [monthStart, monthEnd]);

    const dueCountFor = (date) => {
      if (!date) return 0;
      return payments.filter(pp => {
        if (!pp.due_date || pp.completed_at) return false; // ← ignore finished
        const d = typeof pp.due_date === "string" ? parseISO(pp.due_date) : new Date(pp.due_date);
        return isSameDay(d, date);
      }).length;
    };


    return (
      <div className="pp-mini-cal">
        <div className="pp-mini-cal-head">
          <button className="btn outline xs" onClick={() => setViewMonth(addMonths(viewMonth, -1))}>‹</button>
          <div className="pp-mini-cal-title">{format(viewMonth, "LLLL yyyy")}</div>
          <button className="btn outline xs" onClick={() => setViewMonth(addMonths(viewMonth, 1))}>›</button>
        </div>

        <div className="pp-mini-cal-grid">
          {["Sun","Mon","Tue","Wed","Thu","Fri","Sat"].map(d => (
            <div key={d} className="pp-mini-cal-dow">{d}</div>
          ))}

          {days.map((date, idx) => {
            const count = dueCountFor(date);
            const isToday = date && isSameDay(date, new Date());
            return (
              <button
                key={idx}
                className={
                  "pp-mini-cal-cell" +
                  (date ? "" : " empty") +
                  (isToday ? " today" : "") +
                  (count > 0 ? " has-due" : "")
                }
                disabled={!date}
                onClick={() => date && onSelectDate?.(date)}
                title={date ? format(date, "PP") : ""}
              >
                {date && <span className="date">{format(date, "d")}</span>}
                {count > 0 && <span className="badge">{count}</span>}
              </button>
            );
          })}
        </div>
      </div>
    );
  }


// ──────────────────────────────────────────────
// Utilities (currency, status text, recurrence calc)
// ──────────────────────────────────────────────
function peso(n) {
  const num = Number(n || 0);
  return num.toLocaleString("en-PH", { style: "currency", currency: "PHP" });
}

function daysStatus(due) {
  const today = new Date();
  const dueDate = typeof due === "string" ? parseISO(due) : due;
  const days = differenceInCalendarDays(dueDate, today);
  if (isSameDay(dueDate, today)) return { label: "Due today", tone: "warn", days };
  if (days < 0)
    return { label: `Overdue: ${Math.abs(days)} day${Math.abs(days) === 1 ? "" : "s"}`, tone: "bad", days };
  return { label: `Due in: ${days} day${days === 1 ? "" : "s"}`, tone: days <= 3 ? "warn" : "ok", days };
}





// ──────────────────────────────────────────────
// Notification helpers
// ──────────────────────────────────────────────
function toDateOnly(d) {
  const dt = typeof d === "string" ? parseISO(d) : new Date(d);
  return new Date(dt.getFullYear(), dt.getMonth(), dt.getDate());
}
function daysUntilDue(due) {
  const today = toDateOnly(new Date());
  const d = toDateOnly(due);
  return differenceInCalendarDays(d, today);
}

// Map a notify string -> days before due date to start reminding
function notifyOffsetDays(pp) {
  const v = (pp?.notify || "none").toLowerCase();
  if (v === "due_day") return 0;
  if (v === "1_day_before") return 1;
  if (v === "3_days_before") return 3;
  if (v === "1_week_before") return 7;
  return null; // 'none' or unknown -> no reminder
}

function toDate(x) {
  return typeof x === "string" ? parseISO(x) : (x ? new Date(x) : null);
}

// Core rule whether a plan should remind "today"
function shouldRemindToday(pp, today) {
  if (!pp || pp.completed_at) return false;
  const due = toDate(pp.due_date);
  if (!due || Number.isNaN(due)) return false;

  const offset = notifyOffsetDays(pp);
  if (offset === null) return false;                 // notifications off

  const startRemind = addDays(due, -offset);

  // snooze/seen gates (work even if columns are missing)
  const snoozeUntil = toDate(pp.snooze_until);
  if (snoozeUntil && (isAfter(snoozeUntil, today) || isSameDay(snoozeUntil, today))) return false;

  const lastSeen = toDate(pp.last_seen_on);
  if (lastSeen && isSameDay(lastSeen, today)) return false;

  // remind if we are on/after the start window and not past due by years (always allow past due)
  return !isBefore(today, startRemind);
}

// match your enum: none, due_date, one_day_before, three_days_before, week_before
function shouldNotifyToday(pp) {
  if (!pp || pp.completed_at || !pp.due_date || pp.notify === "none") return false;
  const diff = daysUntilDue(pp.due_date);
  switch (pp.notify) {
    case "due_date": return diff === 0;
    case "one_day_before": return diff === 1 || diff === 0; // include same-day grace
    case "three_days_before": return diff <= 3 && diff >= 0;
    case "week_before": return diff <= 7 && diff >= 0;
    default: return false;
  }
}

// Normalize plan tax for expense creation: default is_inclusive = true
function normalizePlanTax(t) {
  if (!t) return null;
  const type = t.type || 'NONE';
  const parsedRate = Number(t.rate);
  const safeRate =
    Number.isFinite(parsedRate)
      ? parsedRate
      : type === 'VAT' ? 0.12
      : type === 'PERCENTAGE_TAX' ? 0.03
      : 0;

  return {
    ...t,
    type,
    rate: safeRate,
    is_inclusive: (t?.is_inclusive ?? true),  // ← default to TRUE
    withholding_rate: Number.isFinite(Number(t?.withholding_rate)) ? Number(t.withholding_rate) : 0,
  };
}



// Uses planned_recurrence first, falls back to frequency on planned_payments
async function computeNextDueDate(pp) {
  const baseDate = pp.due_date
    ? (typeof pp.due_date === "string" ? parseISO(pp.due_date) : new Date(pp.due_date))
    : new Date();
  
  const today = startOfDay(new Date());

  // 1) Try a row in planned_recurrence
  const { data: rec, error: recErr } = await supabase
    .from("planned_recurrence")
    .select("repeat, every, until_date, occurrences_count")
    .eq("planned_payment_id", pp.id)
    .maybeSingle();

    if (!recErr && rec) {
      const every = Number(rec.every || 1);
      const unit = String(rec.repeat || "").toLowerCase();

      // count existing expenses tied to this plan
      const { count: doneCount } = await supabase
        .from("expenses")
        .select("id", { count: "exact", head: true })
        .eq("planned_payment_id", pp.id);

      // 1.1) Check Occurrence Limit
      if (Number.isFinite(rec.occurrences_count)) {
        const max = Number(rec.occurrences_count);
        if ((doneCount || 0) >= max) return null;   
      }
      
      // 1.2) Calculate the next due date, starting by advancing one period
      let next = baseDate;
      let iterations = 0; // Safety guard
      
      // Step 1: Advance by one interval from the last paid/due date. This fixes the bug when baseDate == today.
      if (unit === "daily") next = addDays(next, every);
      else if (unit === "weekly") next = addDays(next, every * 7);
      else if (unit === "monthly") next = addMonths(next, every);
      else if (unit === "yearly") next = addYears(next, every);
      else return null; // Unknown unit, stop
        
      // Step 2: Now, if the result is still in the past (e.g., paid very late), advance until the future.
      while (isBefore(next, today) && iterations < 365) { 
        if (unit === "daily") next = addDays(next, every);
        else if (unit === "weekly") next = addDays(next, every * 7);
        else if (unit === "monthly") next = addMonths(next, every);
        else if (unit === "yearly") next = addYears(next, every);
        
        iterations++;
      }
      
      // If we jumped too far ahead, it might be an error or very old plan, but for now, rely on `next`

      // 1.3) Check Until Date Limit
      if (next && rec.until_date) {
        const until = typeof rec.until_date === "string" ? parseISO(rec.until_date) : new Date(rec.until_date);
        if (isAfter(next, until)) return null;
      }
      
      if (next) return next;
    }
    
    // If it was a one-time payment or no recurrence rule, it's done.
    return null;
  }

// ──────────────────────────────────────────────
// Components: Modal, PaymentForm, PlannedPaymentRow, Section
// ──────────────────────────────────────────────
function Modal({ open, title, children, onClose }) {
  if (!open) return null;
  return (
    <div
      className="modal-overlay fancy"
      role="dialog"
      aria-modal="true"
      onMouseDown={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div className="modal sheet animate-in">
        <div className="modal-header">
          <div className="modal-title">{title}</div>
          <button className="icon-btn" onClick={onClose} aria-label="Close">✕</button>
        </div>
        <div className="modal-body">{children}</div>
        <div className="modal-footer">
          <button className="btn secondary" onClick={onClose}>Close</button>
        </div>
      </div>
    </div>
  );
}

function CompletedList({ items, onEdit }) {
  return (
    <section className="pp-section">
      <h3 className="pp-section-title">Completed Plans</h3>
      <div className="pp-section-list scroll-y">
        {items.length === 0 ? (
          <div className="pp-card pp-empty">No completed plans yet.</div>
        ) : (
          items.map((pp) => (
            <div key={pp.id} className="pp-card">
              <div className="pp-card-left">
                <div className="pp-name">{pp.name}</div>
                <div className="pp-category">{pp.category_name || "—"}</div>
                <div className="pp-status ok">Completed {pp.completed_at ? format(new Date(pp.completed_at), "PP") : ""}</div>
              </div>
              <div className="pp-card-right">
                {Number.isFinite(pp?.targetCount) && (
                  <div className="pp-progress">
                    <span className="pp-progress-badge">{pp.doneCount}/{pp.targetCount} paid</span>
                  </div>
                )}
                <div className="pp-actions">
                  <button className="btn outline" onClick={() => onEdit(pp)}>View / Edit</button>
                </div>
              </div>
            </div>
          ))
        )}
      </div>
    </section>
  );
}


function PaymentForm({
  initial,
  initialRecurrence,
  onSubmit,
  onCancel,
  categories,
  contacts,
  labels,
  onDelete,
  onMarkFinished,
}) {
  const [form, setForm] = useState(() => ({
    name: initial?.name || "",
    amount: initial?.amount || "",
    main_category_id: initial?.category?.parent_id || initial?.category_parent_id || "",
    sub_category_id: initial?.category_id || "",
    category_id: initial?.category_id || "",

    contact_id: initial?.contact_id || "",
    frequency: (initialRecurrence || initial?.frequency === "recurrent") ? "recurring" : "one-time",
    due_date: initial?.due_date
      ? format(typeof initial.due_date === "string" ? new Date(initial.due_date) : initial.due_date, "yyyy-MM-dd")
      : "",

    notes: initial?.notes || "",
    label_id: initial?.label_id || "",
    notify: initial?.notify || "none",

    // Recurrence defaults per your enums
    recurrence_repeat: initialRecurrence?.repeat || "monthly",  // default = Repeat Monthly
    recurrence_every: initialRecurrence?.every ?? 1,            // default = 1
    recurrence_mode:
      initialRecurrence
        ? (initialRecurrence.occurrences_count != null ? "occurrences"
          : (initialRecurrence.until_date ? "until" : "forever"))
        : "forever",
    recurrence_until_date: initialRecurrence?.until_date
      ? format(typeof initialRecurrence.until_date === "string"
          ? new Date(initialRecurrence.until_date)
          : initialRecurrence.until_date, "yyyy-MM-dd")
      : "",
    recurrence_occurrences: initialRecurrence?.occurrences_count ?? "",
  }));

    // NEW: State for inline errors
  const [errors, setErrors] = useState({});
  const [taxType, setTaxType] = useState(initial?.tax_json?.type || "NONE");
  const [taxRate, setTaxRate] = useState(
    Number.isFinite(Number(initial?.tax_json?.rate)) ? Number(initial.tax_json.rate) : 0.03

  );


  const mains = useMemo(
    () => categories.filter(c => !c.parent_id),
    [categories]
  );
  const subs = useMemo(
    () => categories.filter(c => c.parent_id === form.main_category_id),
    [categories, form.main_category_id]
  );

  const change = (e) => setForm((s) => ({ ...s, [e.target.name]: e.target.value }));

  const updateMainCategory = (e) => {
    const v = e.target.value;
    setForm(s => ({
      ...s,
      main_category_id: v,
      sub_category_id: "",     // reset sub when main changes
      // When main changes, clear any main category error
    }));
  };

  const submit = (e) => {
    e.preventDefault();
    const newErrors = {};

    // 1) Name required
    if (!form.name.trim()) { newErrors.name = "Name is required."; }

    // 2) Amount > 0 and < 10,000,000
    const amt = Number(form.amount);
    if (!Number.isFinite(amt) || amt <= 0) { newErrors.amount = "Amount must be greater than 0."; }
    else if (amt >= 10_000_000) { newErrors.amount = "Amount must be less than 10,000,000."; }

    // 3) Main category required
    if (!form.main_category_id) { newErrors.main_category_id = "Main category is required."; }

    // 4) Due date required
    if (!form.due_date) { newErrors.due_date = "Due date is required."; }

    // 5) Recurrence-specific required fields
    if (form.frequency === "recurring") {
      if (!form.recurrence_repeat) { newErrors.recurrence_repeat = "Repeat unit is required."; }
      
      const everyNum = Number(form.recurrence_every || 1);
      if (!Number.isFinite(everyNum) || everyNum < 1) { newErrors.recurrence_every = "Every must be at least 1."; }

      if (form.recurrence_mode === "until" && !form.recurrence_until_date) {
        newErrors.recurrence_until_date = "Please pick an 'Until' date.";
      }
      if (form.recurrence_mode === "occurrences") {
        const occ = Number(form.recurrence_occurrences);
        if (!Number.isFinite(occ) || occ < 1) { newErrors.recurrence_occurrences = "Occurrences must be at least 1."; }
      }
    }
    
    setErrors(newErrors);

    if (Object.keys(newErrors).length > 0) {
      // alert("Please correct the errors in the form."); // Removed legacy alert
      return;
    }

    // Build recurrence payload for parent
    let recurrence = null;
    if (form.frequency === "recurring") {
      const mode = form.recurrence_mode; // "forever" | "until" | "occurrences"
      const duration =
        mode === "until" ? "until_date" :
        mode === "occurrences" ? "count" :
        "forever";

      recurrence = {
        repeat: form.recurrence_repeat,                         // daily|weekly|monthly|yearly
        every: Number(form.recurrence_every || 1),             // >= 1
        duration,                                              // <<— NEW: recur_duration enum
        until_date: duration === "until_date" ? form.recurrence_until_date : null,
        occurrences_count: duration === "count" ? Number(form.recurrence_occurrences) : null,
      };
    }


    // Normalize final category_id: sub if chosen, else main
    const finalCategoryId = form.sub_category_id || form.main_category_id;

    // Map UI frequency to DB enum
    const dbFrequency = form.frequency === "recurring" ? "recurrent" : "one_time";


    onSubmit({
      name: form.name.trim(),
      amount: amt,
      category_id: finalCategoryId,
      contact_id: form.contact_id || null,
      frequency: form.frequency,
      due_date: form.due_date,
      notes: form.notes || null,
      label_id: form.label_id || null,
      notify: form.notify || "none",
      recurrence, // may be null for one-time
        tax_json: taxType === "NONE" ? null : {
        type: taxType,
        rate: Number(taxRate || 0),
        is_inclusive: true,
      },
    });
  };


  return (
    <form onSubmit={submit} className="pp-form">
      <div className="fields-grid">
        <div className="field">
          <label>Name</label>
          <input name="name" value={form.name} onChange={change} placeholder="e.g., Internet Bill" className={errors.name ? "input-error" : ""} />
          {errors.name && <p className="error-message">{errors.name}</p>}
        </div>
        <div className="field">
          <label>Amount (PHP)</label>
          <input name="amount" type="number" step="0.01" value={form.amount} onChange={change} className={errors.amount ? "input-error" : ""} />
          {errors.amount && <p className="error-message">{errors.amount}</p>}
        </div>
        <div className="field">
          <label>Main Category</label>
          <select
            name="main_category_id"
            value={form.main_category_id || ""}
            onChange={updateMainCategory} // Use new custom handler
            required
            className={errors.main_category_id ? "input-error" : ""}
          >
            <option value="">Choose a category</option>
            {mains.map((c) => (<option key={c.id} value={c.id}>{c.name}</option>))}
          </select>
          {errors.main_category_id && <p className="error-message">{errors.main_category_id}</p>}
        </div>

        {/* Subcategory (optional; filtered by main) */}
        <div className="field">
          <label>Subcategory (optional)</label>
          <select
            name="sub_category_id"
            value={form.sub_category_id || ""}
            onChange={(e) => setForm(s => ({ ...s, sub_category_id: e.target.value }))}
            disabled={!form.main_category_id}
          >
            <option value="">Choose a sub-category</option>
            {subs.map((c) => (<option key={c.id} value={c.id}>{c.name}</option>))}
          </select>
        </div>

        <div className="field">
          <label>Contact</label>
          <select name="contact_id" value={form.contact_id || ""} onChange={change}>
            <option value="">—</option>
            {contacts.map((c) => (<option key={c.id} value={c.id}>{c.name}</option>))}
          </select>
        </div>
        <div className="field">
          <label>Frequency</label>
          <select name="frequency" value={form.frequency} onChange={change}>
            <option value="one-time">One-time</option>
            <option value="recurring">Recurrent Payment</option>
          </select>
        </div>
        <div className="field">
          <label>Due Date</label>
          <input name="due_date" type="date" value={form.due_date} onChange={(e)=>setForm(s=>({...s,due_date:e.target.value}))} required className={errors.due_date ? "input-error" : ""} />
          {errors.due_date && <p className="error-message">{errors.due_date}</p>}
        </div>
        <div className="field">
          <label>Label</label>
          <select name="label_id" value={form.label_id || ""} onChange={change}>
            <option value="">—</option>
            {labels.map((l) => (<option key={l.id} value={l.id}>{l.name}</option>))}
          </select>
        </div>
        <div className="field">
          <label>Notify</label>
          <select value={form.notify || "none"} onChange={e => setForm(v => ({ ...v, notify: e.target.value }))}>
            <option value="none">None</option>
            <option value="due_date">On due date</option>
            <option value="one_day_before">1 day before</option>
            <option value="three_days_before">3 days before</option>
            <option value="week_before">7 days before</option>
          </select>
        </div>
        {/* Tax settings for planned payment */}
        <div className="field">
          <label>Tax Type</label>
          <select
            name="taxType"
            value={taxType}
            onChange={(e) => {
              const v = e.target.value;
              setTaxType(v);
              if (v === "VAT") setTaxRate(0.12);
              else if (v === "PERCENTAGE_TAX") setTaxRate(0.03);
              else setTaxRate(0);
            }}
          >
            <option value="NONE">None</option>
            <option value="PERCENTAGE_TAX">Percentage Tax (3%)</option>
            <option value="VAT">VAT (12%)</option>
          </select>
        </div>

        {taxType !== "NONE" && (
          <div className="field">
            <label>Tax Rate</label>
            <input
              type="number"
              step="0.0001"
              value={taxRate}
              onChange={(e) => setTaxRate(Number(e.target.value || 0))}
            />
            <div className="hint">Default is 0.03 for Percentage, 0.12 for VAT. Editable.</div>
          </div>
        )}

        <div className="field" style={{ gridColumn: "1 / -1" }}>
          <label>Notes</label>
          <textarea name="notes" rows={3} value={form.notes} onChange={change} placeholder="Optional notes" />
        </div>

    {/* Recurrence Config — only if recurring */}
        {form.frequency === "recurring" && (
          <>
            {/* Friendlier Repeat labels */}
            <div className="field">
              <label>Repeat</label>
              <select
                name="recurrence_repeat"
                value={form.recurrence_repeat}
                onChange={(e)=>setForm(s=>({...s, recurrence_repeat: e.target.value }))}
                required
                className={errors.recurrence_repeat ? "input-error" : ""}
              >
                <option value="daily">Repeat Daily</option>
                <option value="weekly">Repeat Weekly</option>
                <option value="monthly">Repeat Monthly</option>
                <option value="yearly">Repeat Yearly</option>
              </select>
              {errors.recurrence_repeat && <p className="error-message">{errors.recurrence_repeat}</p>}
            </div>


            {/* Every ___ <unit> */}
            <div className="field">
              <label>
                {`Every ${form.recurrence_every || 1} `}
                {form.recurrence_repeat === "daily" ? "day(s)" :
                form.recurrence_repeat === "weekly" ? "week(s)" :
                form.recurrence_repeat === "monthly" ? "month(s)" : "year(s)"}
              </label>
              <input
                name="recurrence_every"
                type="number"
                min="1"
                step="1"
                value={form.recurrence_every}
                onChange={(e)=>setForm(s=>({...s, recurrence_every: e.target.value }))}
                required
                className={errors.recurrence_every ? "input-error" : ""}
              />
              {errors.recurrence_every && <p className="error-message">{errors.recurrence_every}</p>}

            </div>

            {/* Recurrency mode */}
            <div className="field">
              <label>Recurrency</label>
              <select
                name="recurrence_mode"
                value={form.recurrence_mode}
                onChange={(e)=>setForm(s=>({...s, recurrence_mode: e.target.value }))}
              >
                <option value="forever">Forever</option>
                <option value="until">Until a date</option>
                <option value="occurrences">For a number of events</option>
              </select>
            </div>

            {form.recurrence_mode === "until" && (
              <div className="field">
                <label>Until Date</label>
                <input
                  name="recurrence_until_date"
                  type="date"
                  value={form.recurrence_until_date}
                  onChange={(e)=>setForm(s=>({...s, recurrence_until_date: e.target.value }))}
                  required
                  className={errors.recurrence_until_date ? "input-error" : ""}
                />
                {errors.recurrence_until_date && <p className="error-message">{errors.recurrence_until_date}</p>}
              </div>
            )}

           {form.recurrence_mode === "occurrences" && (
              <div className="field">
                <label>Occurrences (max)</label>
                <input
                  name="recurrence_occurrences"
                  type="number"
                  min="1"
                  step="1"
                  value={form.recurrence_occurrences}
                  onChange={(e)=>setForm(s=>({...s, recurrence_occurrences: e.target.value }))}
                  required
                  className={errors.recurrence_occurrences ? "input-error" : ""}
                />
                {errors.recurrence_occurrences && <p className="error-message">{errors.recurrence_occurrences}</p>}
              </div>
            )}
          </>
        )}

      </div>

      <div className="modal-footer" style={{ padding: 0, borderTop: "none", marginTop: 8, justifyContent: "space-between" }}>
        {/* Left side actions */}
        {initial?.id && (
          <div className="pp-admin-actions">
            {/* Show 'Mark as Finished' only if it's recurrent and not completed */}
            {initial.frequency === "recurrent" && !initial.completed_at && (
              <button
                type="button"
                className="btn secondary warn"
                onClick={() => onMarkFinished(initial)}
              >
                Mark as Finished
              </button>
            )}
            <button
              type="button"
              className="btn secondary danger"
              onClick={() => onDelete(initial)}
            >
              Delete
            </button>
          </div>
        )}
        
        {/* Right side save/cancel */}
        <div>
          <button type="button" className="btn secondary" onClick={onCancel}>Cancel</button>
          <button type="submit" className="btn primary">{initial?.id ? "Save changes" : "Create"}</button>
        </div>
      </div>
    </form>
  );
}

function PlannedPaymentRow({ pp, onPaid, onEdit }) {
  const status = daysStatus(pp.due_date);
  const categoryName = pp.category_name || pp.category?.name || "—";

  const pct = Number.isFinite(pp?.targetCount) && pp.targetCount > 0
    ? Math.min(100, Math.round((Number(pp.doneCount || 0) / Number(pp.targetCount)) * 100))
    : null;

  const onPayNow = async () => {
    const confirmPay = window.confirm(`Create expense for ${peso(pp.amount)} and mark as paid?`);
    if (!confirmPay) return;

    // create expense
    const occurred_on = format(new Date(), "yyyy-MM-dd");
    const { data: expense, error } = await supabase
      .from("expenses")
      .insert({
        user_id: pp.user_id,
        occurred_on,
        category_id: pp.category_id,
        amount: pp.amount,
        notes: `Planned payment • ${pp.name}`,
        planned_payment_id: pp.id,
        tax_json: normalizePlanTax(pp.tax_json),
      })
      .select("id")
      .single();

    if (error) { console.error(error); alert("Failed to pay. Please try again."); return; }

    // compute next recurrence due date (if any)
    const nextDue = await computeNextDueDate(pp);

    // NOTE: do NOT set due_date = null (DB constraint blocks it)
    const patch = { expense_id: expense.id };
    if (nextDue) {
      patch.due_date = format(nextDue, "yyyy-MM-dd");
      patch.completed_at = null;          // make sure it's not marked finished
    } else {
      patch.completed_at = new Date().toISOString();  // mark FINISHED
    }

    await supabase.from("planned_payments").update(patch).eq("id", pp.id);
    onPaid?.();
  };

  return (
      <div className="pp-card">
        <div className="pp-card-left">
          <div className="pp-name">{pp.name}</div>
          <div className="pp-category">{categoryName}</div>
          <div className={`pp-status ${status.tone}`}>{status.label}</div>
        </div>

        <div className="pp-card-right">
          <div className="pp-amount">{peso(pp.amount)}</div>

          {/* progress badge for “count” plans */}
          {Number.isFinite(pp?.targetCount) && (
            <div className="pp-progress">
              <span className="pp-progress-badge">{pp.doneCount}/{pp.targetCount} paid</span>
              <div className="pp-progress-bar">
                <div className="pp-progress-fill" style={{ width: `${pct}%` }} />
              </div>
            </div>
          )}

          {/* optional: you can hide this old frequency chip if it’s confusing */}
          {/* {pp.frequency && <div className="pp-frequency">{String(pp.frequency)}</div>} */}

          <div className="pp-actions">
            <button className="btn primary" onClick={onPayNow}>Pay now</button>
            <button className="btn outline" onClick={() => onEdit(pp)}>Edit</button>
          </div>
        </div>
      </div>
    );
  }

function Section({ title, items, onPaid, onEdit }) {
  return (
    <section className="pp-section">
      <h3 className="pp-section-title">{title}</h3>
      <div className="pp-section-list scroll-y">
        {items.length === 0 ? (
          <div className="pp-card pp-empty">No items</div>
        ) : (
          items.map((pp) => (
            <PlannedPaymentRow key={pp.id} pp={pp} onPaid={onPaid} onEdit={onEdit} />
          ))
        )}
      </div>
    </section>
  );
}

// ──────────────────────────────────────────────
// Main Page
// ──────────────────────────────────────────────
export default function PlannedPaymentsPage() {
  const navigate = useNavigate();

  const [loading, setLoading] = useState(true);
  const [items, setItems] = useState([]);
  const [history, setHistory] = useState([]);
  const [error, setError] = useState(null);

  const [openCreate, setOpenCreate] = useState(false);
  const [editItem, setEditItem] = useState(null);
  const [currentRecurrence, setCurrentRecurrence] = useState(null);

  const [categories, setCategories] = useState([]);
  const [contacts, setContacts] = useState([]);
  const [labels, setLabels] = useState([]);

  const today = new Date();
  const weekEnd = endOfWeek(today, { weekStartsOn: 1 }); // Mon-Sun
  const nextMonthStart = startOfMonth(addMonths(today, 1));
  const nextMonthEnd = endOfMonth(addMonths(today, 1));

  const [completed, setCompleted] = useState([]);
  const [showCompleted, setShowCompleted] = useState(true); // collapsible

  const [reminders, setReminders] = useState([]);     // items to show today




async function snoozeOneDay(id) {
  const tomorrow = addDays(startOfDay(new Date()), 1);
  const tomorrowStr = tomorrow.toISOString().slice(0, 10);

  // Optimistic UI: Remove from reminders
  setReminders(rs => rs.filter(r => r.id !== id));

  try {
    const { error } = await supabase
      .from("planned_payments")
      .update({ snooze_until: tomorrowStr })
      .eq("id", id);
    if (error) throw error;

    // Pessimistic/Corrective UI: Update the main list item's state
    setItems(currentItems => currentItems.map(p =>
      p.id === id ? { ...p, snooze_until: tomorrowStr } : p
    ));
  } catch (e) {
    console.warn("snoozeOneDay failed", e);
  }
}

async function markSeenToday(id) {
  const todayStr = startOfDay(new Date()).toISOString().slice(0, 10);

  // Optimistic UI: Remove from reminders
  setReminders(rs => rs.filter(r => r.id !== id));

  try {
    const { error } = await supabase
      .from("planned_payments")
      .update({ last_seen_on: todayStr })
      .eq("id", id);
    if (error) throw error;

    // Pessimistic/Corrective UI: Update the main list item's state
    setItems(currentItems => currentItems.map(p =>
      p.id === id ? { ...p, last_seen_on: todayStr } : p
    ));
  } catch (e) {
    console.warn("markSeenToday failed", e);
  }
}



  async function loadLookups() {
    const [{ data: cats }, { data: cons }, { data: labs }] = await Promise.all([
      supabase.from("categories").select("id, name, parent_id").order("name"),
      supabase.from("contacts").select("id, name").order("name"),
      supabase.from("labels").select("id, name").order("name"),
    ]);
    setCategories(cats || []);
    setContacts(cons || []);
    setLabels(labs || []);
    return { cats: cats || [], cons: cons || [], labs: labs || [] };
  }

  const refresh = async () => {
    setLoading(true);
    setError(null);
    try {
      const { cats } = await loadLookups();

    // planned payments
    const { data: planned, error: e1 } = await supabase
      .from("planned_payments")
      .select("*")
      .order("due_date", { ascending: true });
    if (e1) throw e1;

    const ids = (planned || []).map(p => p.id);

    // fetch occurrences target per plan
    const { data: recs } = await supabase
      .from("planned_recurrence")
      .select("planned_payment_id, occurrences_count")
      .in("planned_payment_id", ids);

    // fetch counts of expenses per plan (for progress X/N)
    // fetch all expense rows (ids only) then group in JS
    // maps
    const occMap = new Map((recs || []).map(r => [r.planned_payment_id, r.occurrences_count]));

    const { data: expenseRows, error: eCounts } = await supabase
      .from("expenses")
      .select("planned_payment_id")
      .not("planned_payment_id", "is", null)
      .in("planned_payment_id", ids);

    if (eCounts) throw eCounts;

    const countMap = new Map();
    for (const row of expenseRows || []) {
      const k = row.planned_payment_id;
      if (!k) continue;
      countMap.set(k, (countMap.get(k) || 0) + 1);
    }

    // map category names (you already had this idea)
    const catMap = new Map((cats || []).map((c) => [c.id, c.name]));

    // augment all with names + progress
    const augmented = (planned || []).map((p) => ({
      ...p,
      category_name: catMap.get(p.category_id),
      doneCount: countMap.get(p.id) || 0,
      targetCount: occMap.get(p.id) ?? null,
    }));

    // after you enrich items in refresh()
    const active = augmented.filter(p => !p.completed_at);
    const finished = augmented
      .filter(p => !!p.completed_at)
      .sort((a,b) => new Date(b.completed_at || 0) - new Date(a.completed_at || 0));

    setItems(active);
    setCompleted(finished);


      // recent past payments from expenses linked to planned payments
      const { data: recent, error: e3 } = await supabase
        .from("expenses")
        .select("id, occurred_on, amount, notes, planned_payment_id")
        .not("planned_payment_id", "is", null)
        .order("occurred_on", { ascending: false })
        .limit(10);
      if (e3) throw e3;

      setHistory(recent || []);
      

    } catch (err) {
      console.error(err);
      setError("Failed to load planned payments.");
    } finally {
      setLoading(false);
    }
  };

  const groups = useMemo(() => {
    const dueThisWeek = [], dueNextMonth = [], upcoming = [];
    for (const pp of items) {
      if (!pp.due_date) continue;                 // keep your existing guard
      if (pp.completed_at) continue;              // NEW: finished plans are not active
      const d = typeof pp.due_date === "string" ? parseISO(pp.due_date) : new Date(pp.due_date);
      if (d <= weekEnd)            dueThisWeek.push(pp);
      else if (d >= nextMonthStart && d <= nextMonthEnd) dueNextMonth.push(pp);
      else if (d > weekEnd)        upcoming.push(pp);
    }
    return { dueThisWeek, dueNextMonth, upcoming };
  }, [items, weekEnd, nextMonthStart, nextMonthEnd]);


    // Map of completed IDs for quick lookups
  const completedMap = useMemo(() => {
    const m = new Map();
    for (const c of completed || []) m.set(c.id, true);
    return m;
  }, [completed]);

  // Flatten the left-column groups
  const leftColumnUnpaid = useMemo(() => [
    ...(groups?.dueThisWeek ?? []),
    ...(groups?.dueNextMonth ?? []),
    ...(groups?.upcoming ?? []),
  ], [groups]);


  const totals = useMemo(() => {
    const today = startOfDay(new Date());

    const totalUpcoming = leftColumnUnpaid.reduce(
      (sum, p) => sum + (Number(p.amount) || 0),
      0
    );

    // If you prefer counting completed plans instead of history-based payments,
    // swap history.length with completed.length.
    const paymentCount = (history?.length ?? 0);

    const dueTodayCount = leftColumnUnpaid.filter((p) =>
      isSameDay(new Date(p.due_date), today)
    ).length;

    return { totalUpcoming, paymentCount, dueTodayCount };
  }, [leftColumnUnpaid, history]);

  useEffect(() => { refresh(); }, []);

  useEffect(() => {
  const today = startOfDay(new Date());
  const pool = items ?? [];
  const dueList = pool.filter(pp => shouldRemindToday(pp, today))
                      .sort((a, b) => new Date(a.due_date) - new Date(b.due_date));
  setReminders(dueList);
  console.debug("[Reminders] computed:", dueList.length);
}, [items]);



  async function handleOpenEdit(pp) {
    try {
      const { data } = await supabase
        .from("planned_recurrence")
        .select("planned_payment_id, repeat, every, until_date, occurrences_count")
        .eq("planned_payment_id", pp.id)
        .maybeSingle();
      setCurrentRecurrence(data || null);
    } catch (e) {
      console.error(e);
      setCurrentRecurrence(null);
    }
    setEditItem(pp);
  }

  async function handleCreate(values) {
    const due = values.due_date || format(new Date(), "yyyy-MM-dd"); // ensure not null

    // 1) insert as one_time (must include due_date)
    const { data: created, error: e1 } = await supabase
      .from("planned_payments")
      .insert({
        name: values.name,
        amount: values.amount,
        category_id: values.category_id || null,
        contact_id: values.contact_id || null,
        frequency: "one_time",
        due_date: due, // <— use ensured value
        notes: values.notes || null,
        label_id: values.label_id || null,
        notify: values.notify || "none",
        tax_json: values.tax_json || null,
      })
      .select("id")
      .single();

    if (e1) { console.error(e1); alert("Create failed"); return; }

    // 2) If UI chose recurring, create recurrence row FIRST
    const r = values.recurrence || {};
    const wantsRecurring =
      values.frequency === "recurring" || (r.repeat && r.repeat !== "");

    if (wantsRecurring) {
      const payload = {
        planned_payment_id: created.id,
        repeat: r.repeat,                          // daily|weekly|monthly|yearly
        every: r.every ?? 1,
        duration: r.duration || "forever",         // <<— NEW (NOT NULL)
        until_date: r.duration === "until_date" ? r.until_date : null,
        occurrences_count: r.duration === "count" ? r.occurrences_count : null,
      };

      const { error: rErr } = await supabase
        .from("planned_recurrence")
        .upsert(payload, { onConflict: "planned_payment_id" });
      if (rErr) { console.error(rErr); alert("Create recurrence failed"); return; }

      const { error: e2 } = await supabase
        .from("planned_payments")
        .update({ frequency: "recurrent" })
        .eq("id", created.id);
      if (e2) { console.error(e2); alert("Finalize as recurrent failed"); return; }
    }


    setOpenCreate(false);
    await refresh();
  }


  async function handleEditSave(values) {
    const r = values.recurrence || {};
    const wantsRecurring =
      values.frequency === "recurring" || (r.repeat && r.repeat !== "");

    if (wantsRecurring) {
      const payload = {
        planned_payment_id: editItem.id,
        repeat: r.repeat,
        every: r.every ?? 1,
        duration: r.duration || "forever",              // <<— NEW
        until_date: r.duration === "until_date" ? r.until_date : null,
        occurrences_count: r.duration === "count" ? r.occurrences_count : null,
      };
      const { error: rErr } = await supabase
        .from("planned_recurrence")
        .upsert(payload, { onConflict: "planned_payment_id" });
      if (rErr) { console.error(rErr); alert("Save recurrence failed"); return; }

      const { error: pErr } = await supabase
        .from("planned_payments")
        .update({
          name: values.name,
          amount: values.amount,
          category_id: values.category_id || null,
          contact_id: values.contact_id || null,
          frequency: "recurrent",
          due_date: editItem.completed_at ? null : (values.due_date || null),
          notes: values.notes || null,
          label_id: values.label_id || null,
          notify: values.notify || "none",
          tax_json: values.tax_json || null,
        })
        .eq("id", editItem.id);
      if (pErr) { console.error(pErr); alert("Update failed"); return; }
    } else {
      // Switching to one-time
      await supabase.from("planned_recurrence").delete().eq("planned_payment_id", editItem.id);
      const { error: pErr } = await supabase
        .from("planned_payments")
        .update({
          name: values.name,
          amount: values.amount,
          category_id: values.category_id || null,
          contact_id: values.contact_id || null,
          frequency: "one_time",
          due_date: editItem.completed_at ? null : (values.due_date || null),
          notes: values.notes || null,
          label_id: values.label_id || null,
          notify: values.notify || "none",
          tax_json: values.tax_json || null,
        })
        .eq("id", editItem.id);
      if (pErr) { console.error(pErr); alert("Update failed"); return; }
    }


    setEditItem(null);
    await refresh();
  }

    async function handleDelete(pp) {
    if (!window.confirm(`Are you sure you want to permanently delete the planned payment: ${pp.name}? This cannot be undone.`)) {
      return;
    }

    try {
      // 1. Delete associated recurrence (if exists)
      await supabase.from("planned_recurrence").delete().eq("planned_payment_id", pp.id);

      // 2. Delete the planned payment
      const { error } = await supabase.from("planned_payments").delete().eq("id", pp.id);

      if (error) throw error;

      setEditItem(null);
      await refresh();
    } catch (e) {
      console.error("Delete failed:", e);
      alert("Failed to delete planned payment.");
    }
  }

  async function handleMarkFinished(pp) {
    if (!window.confirm(`Mark ${pp.name} as permanently finished? This will stop future payments.`)) {
      return;
    }

    try {
      // 1. Delete associated recurrence (stops future recurrence logic)
      await supabase.from("planned_recurrence").delete().eq("planned_payment_id", pp.id);

      // 2. Mark the plan as completed
      const { error } = await supabase
        .from("planned_payments")
        .update({
          completed_at: new Date().toISOString(),
          frequency: "one_time" // Clean up frequency state
        })
        .eq("id", pp.id);

      if (error) throw error;

      setEditItem(null);
      await refresh();
    } catch (e) {
      console.error("Mark finished failed:", e);
      alert("Failed to mark plan as finished.");
    }
  }

  async function handlePayNowFromReminder(pp) {
  const confirmPay = window.confirm(`Create expense for ${peso(pp.amount)} and mark ${pp.name} as paid?`);
  if (!confirmPay) return;
  
  // This logic is copied directly from PlannedPaymentRow's onPayNow
  try {
      // 1. Create expense
      const occurred_on = format(new Date(), "yyyy-MM-dd");
      const { data: expense, error } = await supabase
        .from("expenses")
        .insert({
          user_id: pp.user_id,
          occurred_on,
          category_id: pp.category_id,
          amount: pp.amount,
          notes: `Planned payment • ${pp.name}`,
          planned_payment_id: pp.id,
          tax_json: normalizePlanTax(pp.tax_json),
        })
        .select("id")
        .single();

      if (error) throw error;

      // 2. Compute next recurrence due date (if any)
      const nextDue = await computeNextDueDate(pp);

      // 3. Update planned_payments record
      const patch = { expense_id: expense.id };
      if (nextDue) {
        patch.due_date = format(nextDue, "yyyy-MM-dd");
        patch.completed_at = null;          
      } else {
        patch.completed_at = new Date().toISOString();  
      }
      
      // Also mark as seen/dismissed since the action was taken
      patch.last_seen_on = startOfDay(new Date()).toISOString().slice(0, 10);

      const { error: patchError } = await supabase.from("planned_payments").update(patch).eq("id", pp.id);
      if (patchError) throw patchError;

      // 4. Update UI
      await refresh(); // Refresh all data to reflect payment and new groups
      
    } catch (e) {
      console.error("Failed to pay from reminder.", e);
      alert("Failed to process payment. Please try again.");
    }
  }


  return (
    <div className="dashboard-container">
      <header className="header-bar">
        <h1 className="header-title">BuiswAIz</h1>
      </header>

      <div className="main-section">
        {/* Sidebar copied style from Expenses page */}
        <aside className="sidebar">
          <div className="nav-section">
            <p className="nav-header">GENERAL</p>
            <ul>
              <li onClick={() => navigate("/Dashboard")}>Dashboard</li>
              <li onClick={() => navigate("/inventory")}>Inventory</li>
              <li onClick={() => navigate("/TablePage")}>Sales</li>
              <li onClick={() => navigate("/expenses")}>Expenses</li>
              
              <li onClick={() => navigate("/assistant")}>AI Assistant</li>
            </ul>
            <p className="nav-header">RELATED</p>
            <ul>
              <li onClick={() => navigate("/supplier")}>Supplier</li>
              <li onClick={() => navigate("/pos")}>Point of Sales</li>
              <li className="active">Planned Payments</li>
            </ul>
          </div>
        </aside>

        {/* Main content */}
        <main className="main">
          <div className="planned-header">
            <div className="toolbar-left">
              <button className="btn primary" onClick={() => setOpenCreate(true)}>+ New Planned Payment</button>
            </div>
            <div className="planned-updated">Updated {format(new Date(), "PP p")}</div>
          </div>
          <div className="planned-sep" />

          {/* Two-column layout: LEFT = lists & completed, RIGHT = overview, past, reminders */}
          <div className="planned-page">
            {/* LEFT COLUMN */}
            <div className="planned-left">
              {loading ? (
                <div className="pp-card pp-empty"><span className="spinner" /> Loading planned payments…</div>
              ) : error ? (
                <div className="pp-card pp-error">{error}</div>
              ) : (
                <>
                  <Section title="Due This Week"  items={groups.dueThisWeek}  onPaid={refresh} onEdit={handleOpenEdit} />
                  <Section title="Due Next Month" items={groups.dueNextMonth} onPaid={refresh} onEdit={handleOpenEdit} />
                  <Section title="Upcoming"       items={groups.upcoming}     onPaid={refresh} onEdit={handleOpenEdit} />
                </>
              )}

              <div style={{ marginTop: 12 }}>
                <button className="btn outline" onClick={() => setShowCompleted(s => !s)}>
                  {showCompleted ? "▾" : "▸"} Completed Plans ({completed.length})
                </button>
              </div>
              {showCompleted && <CompletedList items={completed} onEdit={handleOpenEdit} />}
            </div>

            {/* RIGHT COLUMN */}
            <aside className="planned-right">
              {/* Overview */}
               <div className="pp-right-card">
                <div className="pp-right-title">Overview</div>

                <div className="pp-kpis">
                  <div className="pp-kpi">
                    <div className="pp-kpi-icon">₱</div>
                    <div className="pp-kpi-meta">
                      <div className="pp-kpi-label">Upcoming Payments</div>
                      <div className="pp-kpi-value">{peso(totals.totalUpcoming)}</div>
                    </div>
                  </div>
                  <div className="pp-kpi">
                    <div className="pp-kpi-icon">✓</div>
                    <div className="pp-kpi-meta">
                      <div className="pp-kpi-label">Payment Count</div>
                      <div className="pp-kpi-value">{totals.paymentCount}</div>
                    </div>
                  </div>
                  <div className={"pp-kpi " + (totals.dueTodayCount > 0 ? "alert" : "")}>
                    <div className="pp-kpi-icon">⚑</div>
                    <div className="pp-kpi-meta">
                      <div className="pp-kpi-label">Due Today</div>
                      <div className="pp-kpi-value">{totals.dueTodayCount}</div>
                    </div>
                  </div>
                </div>

                {/* Progress: Due Today vs All Active */}
                <div className="pp-progress-block">
                  <div className="pp-progress-top">
                    <span>Today’s Load</span>
                    <span>{leftColumnUnpaid.length > 0
                      ? Math.min(100, Math.round((totals.dueTodayCount / leftColumnUnpaid.length) * 100))
                      : 0}%</span>
                  </div>
                  <div className="pp-progress-bar">
                    <div
                      className="pp-progress-fill"
                      style={{ width: `${
                        leftColumnUnpaid.length > 0
                          ? Math.min(100, Math.round((totals.dueTodayCount / leftColumnUnpaid.length) * 100))
                          : 0
                      }%` }}
                    />
                  </div>
                  <div className="pp-progress-sub">
                    {totals.dueTodayCount} due today • {leftColumnUnpaid.length} active
                  </div>
                </div>
              </div>



                            {/* Calendar Reminders */}
              <div className="pp-right-card" data-test="calendar-card">
                <div className="pp-right-title">Payment Calendar</div>
                      <PlannedCalendarInline
                      payments={(items || [])}
                      onSelectDate={(date) => {
                      const dueList = (items || []).filter(p => {
                        if (!p.due_date) return false;
                        const d = typeof p.due_date === "string" ? parseISO(p.due_date) : new Date(p.due_date);
                        return isSameDay(d, date);
                      });
                      if (dueList.length === 0) return;

                      // Open your existing edit modal on single, or show a quick picker on multiple
                      if (dueList.length === 1) {
                        // Reuse your existing flow to open edit:
                        // handleOpenEdit is already in this file.
                        // @ts-ignore
                        handleOpenEdit(dueList[0]);
                      } else {
                        // Quick summary for now; you can swap to a modal later
                        alert(
                          `Planned payments for ${format(date, "PP")}:\n` +
                          dueList.map(p => `• ${p.name} – ${peso(p.amount)}`).join("\n")
                        );
                      }
                    }}
                  />

              </div>

              {/* Past Payments */}
              <div className="pp-right-card">
                <div className="pp-right-title">Past Payments</div>
                {history.length === 0 ? (
                  <div className="pp-empty">No past payments yet.</div>
                ) : (
                  <ul className="pp-history scroll-y right">
                    {history.map((h) => (
                      <li key={h.id} className="pp-history-row">
                        <div>
                          <div className="pp-history-title">{h.notes || "Payment"}</div>
                          <div className="pp-history-sub">{format(new Date(h.occurred_on), "PP")}</div>
                        </div>
                        <div className="pp-history-amt">{peso(h.amount)}</div>
                      </li>
                    ))}
                  </ul>
                )}
              </div>
            </aside>
          </div>
        </main>
      </div>

      {/* Create Modal */}
      <Modal open={openCreate} title="New Planned Payment" onClose={() => setOpenCreate(false)}>
        <PaymentForm
          initial={null}
          initialRecurrence={null}
          onSubmit={handleCreate}
          onCancel={() => setOpenCreate(false)}
          categories={categories}
          contacts={contacts}
          labels={labels}
          // No onDelete/onMarkFinished for creation
        />
      </Modal>

      {/* Edit Modal */}
      <Modal open={!!editItem} title="Edit Planned Payment" onClose={() => setEditItem(null)}>
        {editItem && (
          <PaymentForm
            initial={editItem}
            initialRecurrence={currentRecurrence}
            onSubmit={handleEditSave}
            onCancel={() => setEditItem(null)}
            categories={categories}
            contacts={contacts}
            labels={labels}
            onDelete={handleDelete}     
            onMarkFinished={handleMarkFinished} 
          />
        )}
      </Modal>
    </div>
  );
}
