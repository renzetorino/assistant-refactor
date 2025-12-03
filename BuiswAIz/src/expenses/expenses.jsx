// ========================= Imports ===========================================
import React, { useEffect, useMemo, useState, useRef } from "react";
import { useCallback } from "react";
import { useNavigate } from "react-router-dom";
import {
  LineChart, Line, XAxis, YAxis, Tooltip, CartesianGrid, ResponsiveContainer,
} from 'recharts';
import Calendar from 'react-calendar';
import 'react-calendar/dist/Calendar.css';
import { supabase } from '../supabase';
import "./ExpenseDashboard.css";
import { fetchMainCategories, fetchSubcategories, getCategoryById } from '../api/categories';
import { listContacts, createContact } from '../api/contacts';
import { listExpensesByMonth, createExpense, updateExpense, listExpensesByYear, listExpensesBetween, deleteExpense } from '../api/expenses';
import { AttachmentsPanel } from '../components/AttachmentsPanel';
import { uploadAttachments } from '../api/attachments';
import BudgetCenter from "../budget/BudgetCenter";
import ContactsCenter from "../contacts/ContactsCenter";
import { listLabels, createLabel, deleteLabel } from '../api/labels';
import ConfirmDeleteModal from "../components/ConfirmDeleteModal";
import TaxCenter from "../tax/TaxCenter";
import { calcTax } from "../libs/tax";



// ========================= Constants & Small Utils ===========================

  const DEFAULT_ATTACHMENTS_BUCKET = import.meta.env.VITE_ATTACHMENTS_BUCKET || "attachments";

// formatYYYYMM, monthBounds, startOfTodayISO, addDaysISO, isoRangeForPreset

  function formatYYYYMM(d) {
    const year = d.getFullYear();
    const month = String(d.getMonth() + 1).padStart(2, '0');
    return `${year}-${month}`;
  }
  function monthBounds(yyyyMM) {
    const [y, m] = yyyyMM.split('-').map(Number);
    const start = new Date(y, m - 1, 1);
    const end = new Date(y, m, 0);
    return { start, end };
  }
  function startOfTodayISO() {
    const d = new Date();
    d.setHours(0,0,0,0);
    return d.toISOString().slice(0,10);
  }
  function addDaysISO(iso, days) {
    const [y,m,dd] = iso.split('-').map(Number);
    const d = new Date(Date.UTC(y, m-1, dd));
    d.setUTCDate(d.getUTCDate() + days);
    return d.toISOString().slice(0,10);
  }
  function isoRangeForPreset(preset) {
    const today = startOfTodayISO();
    switch (preset) {
      case 'last7':   return { start: addDaysISO(today, -7),  end: addDaysISO(today, 1) };
      case 'last30':  return { start: addDaysISO(today, -30), end: addDaysISO(today, 1) };
      case 'last12w': return { start: addDaysISO(today, -84), end: addDaysISO(today, 1) };
      case 'last6m':  return { start: addDaysISO(today, -183), end: addDaysISO(today, 1) };
      case 'last1y':  return { start: addDaysISO(today, -365), end: addDaysISO(today, 1) };
      case 'thisWeek': {
        const d = new Date();
        const day = (d.getDay() + 6) % 7; // 0..6 Monday..Sunday
        const start = addDaysISO(startOfTodayISO(), -day);
        const end   = addDaysISO(startOfTodayISO(), 1);
        return { start, end };
      }
      case 'thisMonth': {
        const now = new Date();
        const start = new Date(Date.UTC(now.getFullYear(), now.getMonth(), 1)).toISOString().slice(0,10);
        const end   = new Date(Date.UTC(now.getFullYear(), now.getMonth()+1, 1)).toISOString().slice(0,10);
        return { start, end };
      }
      case 'thisYear': {
        const now = new Date();
        const start = new Date(Date.UTC(now.getFullYear(), 0, 1)).toISOString().slice(0,10);
        const end   = new Date(Date.UTC(now.getFullYear()+1, 0, 1)).toISOString().slice(0,10);
        return { start, end };
      }
      default: return null;
    }
  }


// ========================= Storage Helpers ===================================
// splitStorageKey, deleteExpenseDeep


// ========================= Notify (no external deps) =========================
// const notify = { success, error };


// ========================= ExpenseDashboard ==================================


  // ---------- State ----------------------------------------------------------
  // (all useState declarations)

  // ---------- Effects: auth guard, pickers, data loads -----------------------
  // (auth guard useEffect)
  // (picker loads useEffects)
  // (table refresh useEffect)
  // (yearRows useEffect)
  // (budget load useEffect)

  // ---------- Data loaders ---------------------------------------------------
  // async function refresh() { ... }
  // async function fetchBudget(yyyyMM) { ... }

  // ---------- Derived selectors ----------------------------------------------
  // categories, filteredByCategory, visibleExpenses, totals, chartData

  // ---------- Handlers --------------------------------------------------------
  // handleOpenExpenseFromContacts
  // openEdit
  // handleSaveExpense
  // handleUpdateExpense
  // handleDeleteExpense
  // handleDeleteLabel
  // onCalendarStartDateChange
  // getInlineAttachmentsFromRow (if you still need it)

  // ---------- Render ----------------------------------------------------------
  // return (...)  (everything in JSX, table, modals, panels)



// ========================= Inline Micro-Components ============================
// QuickContactInline
// QuickLabelInline


// ========================= Export ============================================








// lightweight notifier so we don't crash if a toast lib isn't present
const notify = {
  success: (msg) => { try { console.log(msg); alert(msg); } catch {} },
  error:   (msg) => { try { console.error(msg); alert(msg); } catch {} },
};


/** Utils */


const ExpenseDashboard = () => {
  const navigate = useNavigate();

  // ======== State ========

  const [budget, setBudget] = useState(0);

  const [editId, setEditId] = useState(null);
  const [editFiles, setEditFiles] = useState([]);
  const [showEditModal, setShowEditModal] = useState(false); 
  const [calendarDate, setCalendarDate] = useState(new Date());
  const [selectedMonth, setSelectedMonth] = useState(formatYYYYMM(new Date()));
  const { start: monthStart, end: monthEnd } = useMemo(
    () => monthBounds(selectedMonth),
    [selectedMonth]
  );
  const [yearRows, setYearRows] = useState([]);
  // range filtering
  const [rangeMode, setRangeMode] = useState('month'); // 'month' | 'preset' | 'custom'
  const [rangePreset, setRangePreset] = useState('thisMonth');
  const [rangeFrom, setRangeFrom] = useState('');
  const [rangeTo, setRangeTo] = useState('');

  // expenses + add/edit
  const [rows, setRows] = useState([]);
  const [selectedId, setSelectedId] = useState(null);

  const [showAddModal, setShowAddModal] = useState(false);
  const [occurredOn, setOccurredOn] = useState(new Date().toISOString().slice(0,10));
  const [amount, setAmount] = useState('');
  const [mainCatId, setMainCatId] = useState('');
  const [subCatId, setSubCatId] = useState('');
  const [expanded, setExpanded] = useState(false);
  const [status, setStatus] = useState('uncleared');
  const [editAttachmentCount, setEditAttachmentCount] = useState(0);
  const [contactId, setContactId] = useState('');
  const [notes, setNotes] = useState('');
  const [labelIds, setLabelIds] = useState([]);
  const [newFiles, setNewFiles] = useState([]);
  const [confirmOpen, setConfirmOpen] = useState(false);
  const confirmRef = useRef({});


  const [taxType, setTaxType] = useState('NONE'); // 'VAT' | 'PERCENTAGE_TAX' | 'NONE'
  const [taxRate, setTaxRate] = useState(0.03);
  const [taxInclusive, setTaxInclusive] = useState(false);
  const [withholdingRate, setWithholdingRate] = useState(0);

  const [editOriginalAmount, setEditOriginalAmount] = useState(0);



  // pickers
  const [mains, setMains] = useState([]);
  const [subs, setSubs] = useState([]);
  const [labels, setLabels] = useState([]);
  const [contacts, setContacts] = useState([]);

  // filters & sort
  const [selectedCategory, setSelectedCategory] = useState('all');
  const [sortMode, setSortMode] = useState('date_desc');

  const handleOpenExpenseFromContacts = useCallback(async (expRowOrId) => {
    // If a full row was passed, use it.
    if (expRowOrId && typeof expRowOrId === "object") {
      openEdit(expRowOrId);
      return;
    }

    // Try local cache, but ONLY if it already has tax_json
    const local = rows.find(r => r.id === expRowOrId);
    if (local && local.tax_json != null) {
      openEdit(local);
      return;
    }

    // Fallback: fetch full record (includes tax_json)
    try {
      const { data, error } = await supabase
        .from("expenses")
        .select("id, occurred_on, amount, notes, status, contact_id, category_id, tax_json")

        .eq("id", expRowOrId)
        .maybeSingle();

      if (error) throw error;
      if (data) {
        openEdit(data);
      } else {
        alert("Could not find that expense record.");
      }
    } catch (e) {
      console.error(e);
      alert(e.message || "Failed to open expense.");
    }
  }, [rows, openEdit]);





function splitStorageKey(key = "") {
  if (!key) return { bucket: DEFAULT_ATTACHMENTS_BUCKET, path: "" };
  const i = key.indexOf("/");
  return i > 0
    ? { bucket: key.slice(0, i), path: key.slice(i + 1) }
    : { bucket: DEFAULT_ATTACHMENTS_BUCKET, path: key };
}

async function deleteExpenseDeep(expenseId) {
  const { data: atts, error: qErr } = await supabase
    .from("attachments")
    .select("id, storage_key")
    .eq("expense_id", expenseId);
  if (qErr) throw qErr;

  const byBucket = new Map();
  for (const a of atts || []) {
    const { bucket, path } = splitStorageKey(a.storage_key || "");
    if (!path) continue;
    if (!byBucket.has(bucket)) byBucket.set(bucket, []);
    byBucket.get(bucket).push(path);
  }
  for (const [bucket, paths] of byBucket) {
    const { error: rmErr } = await supabase.storage.from(bucket).remove(paths);
    if (rmErr) console.warn("Storage remove warning:", bucket, rmErr.message);
  }

  const { error: delAttErr } = await supabase
    .from("attachments")
    .delete()
    .eq("expense_id", expenseId);
  if (delAttErr) throw delAttErr;

  const { error: delExpErr } = await supabase
    .from("expenses")
    .delete()
    .eq("id", expenseId);
  if (delExpErr) throw delExpErr;
}

  // Ensure a label with given name exists; return its id.
  // Also keeps local `labels` state fresh.
  async function ensureLabelByName(name, color = "#ef4444") {
    const existing = labels.find(l => (l.name || "").toLowerCase() === name.toLowerCase());
    if (existing) return existing.id;

    const created = await createLabel({ name, color });  // already imported
    const next = await listLabels();
    setLabels(next);
    const again = next.find(l => (l.name || "").toLowerCase() === name.toLowerCase());
    return again?.id || created?.id;
  }



  // ======== Auth guard ========
  useEffect(() => {
    let isMounted = true;
    (async () => {
      const { data: { user }, error } = await supabase.auth.getUser();
      if (!isMounted) return;
      if (error || !user) {
        window.location.href = '/login';
      }
    })();
    return () => { isMounted = false; };
  }, []);

  // ======== Load pickers ========
  useEffect(() => {
    fetchMainCategories().then(setMains);
    listLabels().then(setLabels);
    listContacts().then(setContacts);
  }, []);
  useEffect(() => {
    if (mainCatId) fetchSubcategories(mainCatId).then(setSubs);
    else { setSubs([]); setSubCatId(''); }
  }, [mainCatId]);

  // ======== Load table data ========
  async function refresh() {
    if (rangeMode === 'month') {
      const [y, m] = selectedMonth.split('-').map(Number);
      const data = await listExpensesByMonth(y, m);
      setRows(data);
    } else if (rangeMode === 'preset') {
      const r = isoRangeForPreset(rangePreset);
      if (r) {
        const data = await listExpensesBetween(r.start, r.end);
        setRows(data);
      }
    } else if (rangeMode === 'custom') {
      if (rangeFrom && rangeTo) {
        const endExcl = addDaysISO(rangeTo, 1);
        const data = await listExpensesBetween(rangeFrom, endExcl);
        setRows(data);
      } else {
        setRows([]);
      }
    }
  }
  useEffect(() => { refresh(); }, [selectedMonth, rangeMode, rangePreset, rangeFrom, rangeTo]);

  useEffect(() => {
    const y = Number(selectedMonth.slice(0,4));
    (async () => setYearRows(await listExpensesByYear(y)))();
  }, [selectedMonth]);

  // ======== Budget for selected month (for summary only) ========
  useEffect(() => {
    setBudget(0);
    fetchBudget(selectedMonth);
  }, [selectedMonth]);

  const fetchBudget = async (yyyyMM) => {
    const monthYear = `${yyyyMM}-01`;
    const { data, error } = await supabase
      .from('budget')
      .select('id, monthly_budget_amount')
      .eq('month_year', monthYear)
      .maybeSingle();

    if (error) {
      console.error("❌ Failed to fetch budget:", error);
      return;
    }
    if (data) {
      const amt = Number(data.monthly_budget_amount) || 0;
      setBudget(amt);
    } else {
      setBudget(0);
    }
  };

  // ======== Derived data ========
  const categories = Array.from(
    new Set((rows || [])
      .map(e => (e.category_path || '').split('/')[0]?.trim())
      .filter(Boolean))
  ).sort((a, b) => a.localeCompare(b));

  const filteredByCategory = selectedCategory === 'all'
    ? rows
    : rows.filter(e => (e.category_path || '').startsWith(selectedCategory));

  const statusOrder = { uncleared: 0, cleared: 1, reconciled: 2 };
  const primaryLabel = (e) => (e?.label_badges?.[0]?.name || '').toLowerCase();

  const visibleExpenses = useMemo(() => {
    const arr = [...filteredByCategory];
    arr.sort((a, b) => {
      switch (sortMode) {
        case 'date_desc':
          return new Date(b.occurred_on) - new Date(a.occurred_on);
        case 'date_asc':
          return new Date(a.occurred_on) - new Date(b.occurred_on);
        case 'amount_desc':
          return Number(b.amount) - Number(a.amount);
        case 'amount_asc':
          return Number(a.amount) - Number(b.amount);
        case 'status_asc':
          return (statusOrder[a.status] ?? 99) - (statusOrder[b.status] ?? 99)
              || new Date(b.occurred_on) - new Date(a.occurred_on);
        case 'status_desc':
          return (statusOrder[b.status] ?? -1) - (statusOrder[a.status] ?? -1)
              || new Date(b.occurred_on) - new Date(a.occurred_on);
        case 'label_asc':
          return primaryLabel(a).localeCompare(primaryLabel(b))
              || new Date(b.occurred_on) - new Date(a.occurred_on);
        case 'label_desc':
          return primaryLabel(b).localeCompare(primaryLabel(a))
              || new Date(b.occurred_on) - new Date(a.occurred_on);
        default:
          return 0;
      }
    });
    return arr;
  }, [filteredByCategory, sortMode]);

  const selectedDateStr = calendarDate.toLocaleDateString('en-CA');
  const dailyTotal = rows
    .filter(e => String(e.occurred_on || '').startsWith(selectedDateStr))
    .reduce((acc, curr) => acc + Number(curr.amount), 0);

  const monthlyTotal = rows.reduce((sum, e) => sum + Number(e.amount), 0);

  const chartData = Array.from({ length: 12 }, (_, month) => {
    const monthName = new Date(0, month).toLocaleString('default', { month: 'short' });
    const total = yearRows
      .filter((e) => new Date(e.occurred_on).getMonth() === month)
      .reduce((sum, e) => sum + Number(e.amount), 0);
    return { month: monthName, total };
  });




  // ======== Budget math for summary cards ========
  const daysInMonth = (() => {
    const d0 = new Date(selectedMonth + "-01");
    return new Date(d0.getFullYear(), d0.getMonth() + 1, 0).getDate();
  })();

  const today = new Date();
  const isCurrentMonth =
    today.getFullYear() === Number(selectedMonth.slice(0, 4)) &&
    today.getMonth() + 1 === Number(selectedMonth.slice(5, 7));

  const daysElapsed = isCurrentMonth ? Math.max(1, today.getDate()) : daysInMonth;

  // Main budget progress
  const spent = monthlyTotal;
  const remaining = Math.max((budget || 0) - spent, 0);
  const overspend = Math.max(spent - (budget || 0), 0);
  const spendPct = budget > 0 ? Math.min(spent / budget, 1) : 0; // clamp 0..1
  const risk =
    budget <= 0 ? "nobudget" :
    spent >= budget ? "over" :
    spent >= budget * 0.8 ? "high" : "ok";

  // Daily pacing vs daily budget
  const dailyBudget = budget > 0 ? budget / daysInMonth : 0;
  const dailyPct = dailyBudget > 0 ? Math.min(dailyTotal / dailyBudget, 1) : 0;

  // Top category (month-to-date)
  const topCat = (() => {
    const sums = new Map(); // main category -> sum
    for (const e of rows) {
      const main = String(e.category_path || "").split("/")[0].trim() || "Uncategorized";
      sums.set(main, (sums.get(main) || 0) + Number(e.amount || 0));
    }
    let name = "—", amt = 0;
    for (const [k, v] of sums) { if (v > amt) { name = k; amt = v; } }
    return { name, amt };
  })();


  // ======== Create / Update / Delete expense ========
  const category_id = subCatId || mainCatId || null;

  async function handleSaveExpense(e) {
    e.preventDefault();
    try {
      const { data: { user } } = await supabase.auth.getUser();
      if (!user) { alert('🚫 Not logged in'); return; }

      const amountNum = Number.parseFloat(String(amount).trim());
      const chosenCategoryId = subCatId || mainCatId || null;
      if (!Number.isFinite(amountNum) || amountNum <= 0 || !occurredOn || !chosenCategoryId) {
        alert('⚠️ Amount, date, and category are required');
        return;
      }

      const taxSnapshot = taxType === 'NONE'
        ? null
        : {
            type: taxType,          // 'VAT' | 'PERCENTAGE_TAX'
            rate: Number(taxRate || 0),
            is_inclusive: !!taxInclusive,
            withholding_rate: Number(withholdingRate || 0),
                 // Optional: store precomputed values for reporting
                  ...(() => {
                    const { net, tax, gross, withholding } = calcTax({
                      amount: Number(amount || 0),
                      type: taxType,
                      rate: Number(taxRate || 0),
                      isInclusive: !!taxInclusive,      // camelCase for helper
                      withholdingRate: Number(withholdingRate || 0)
                    });
                    return {
                      net: Number((net ?? 0).toFixed(2)),
                      tax: Number((tax ?? 0).toFixed(2)),
                      gross: Number((gross ?? 0).toFixed(2)),
                      withholding: Number((withholding ?? 0).toFixed(2)),
                    };
                  })(),
          };
      
            // --- Budget guardrail + auto-label ---
      let finalLabelIds = expanded ? [...labelIds] : [];
      if (Number(budget || 0) > 0) {
        const projected = Number(spent || 0) + amountNum;
        if (projected > Number(budget)) {
          const overBy = projected - Number(budget);
          const ok = confirm(`⚠️ This will put you over budget by ₱${overBy.toFixed(2)}.\nProceed?`);
          if (!ok) return; // cancel create
          // Ensure "Over budget" label is present
          const overId = await ensureLabelByName("Over budget", "#ef4444");
          if (overId && !finalLabelIds.includes(overId)) finalLabelIds.push(overId);
        }
      }
    

      const exp = await createExpense({
        tax_json: taxSnapshot,
        occurred_on: occurredOn,
        amount: amountNum,
        category_id: chosenCategoryId,
        notes: expanded ? (notes || null) : null,
        status: expanded ? status : 'uncleared',
        contact_id: expanded && contactId ? contactId : null,
        label_ids: finalLabelIds,
      });

      if (newFiles.length) {
        await uploadAttachments(exp.id, newFiles);
      }

      setShowAddModal(false);
      setTaxType('NONE');
      setTaxRate(0.03);
      setTaxInclusive(false);
      setWithholdingRate(0);
      setAmount('');
      setMainCatId('');
      setSubCatId('');
      setNotes('');
      setLabelIds([]);
      setContactId('');
      setStatus('uncleared');
      setNewFiles([]);

      await refresh();
    } catch (err) {
      console.error(err);
      alert(`❌ Failed to add expense: ${err?.message || 'Unknown error'}`);
    }
  }

  async function handleUpdateExpense(e) {
    e.preventDefault();
    try {
      const { data: { user } } = await supabase.auth.getUser();
      if (!user) { alert('🚫 Not logged in'); return; }

      const amountNum = Number.parseFloat(String(amount).trim());
      const chosenCategoryId = subCatId || mainCatId || null;
      if (!editId) { alert('Missing expense id'); return; }
      if (!Number.isFinite(amountNum) || amountNum <= 0 || !occurredOn || !chosenCategoryId) {
        alert('⚠️ Amount, date, and category are required');
        return;
      }

      const taxSnapshot = taxType === 'NONE'
        ? null
        : {
            type: taxType,          // 'VAT' | 'PERCENTAGE_TAX'
            rate: Number(taxRate || 0),
            is_inclusive: !!taxInclusive,
            withholding_rate: Number(withholdingRate || 0),
                 // Optional: store precomputed values for reporting
                  ...(() => {
                    const { net, tax, gross, withholding } = calcTax({
                      amount: Number(amount || 0),
                      type: taxType,
                      rate: Number(taxRate || 0),
                      isInclusive: !!taxInclusive,      // camelCase for helper
                      withholdingRate: Number(withholdingRate || 0)
                    });
                    return {
                      net: Number((net ?? 0).toFixed(2)),
                      tax: Number((tax ?? 0).toFixed(2)),
                      gross: Number((gross ?? 0).toFixed(2)),
                      withholding: Number((withholding ?? 0).toFixed(2)),
                    };
                  })(),
          };
 
          // --- Budget guardrail + auto-label for EDIT ---
    let finalEditLabelIds = [...labelIds];
    if (Number(budget || 0) > 0) {
      const projected = Number(spent || 0) - Number(editOriginalAmount || 0) + amountNum;
      if (projected > Number(budget)) {
        const overBy = projected - Number(budget);
        const ok = confirm(`⚠️ This change will put you over budget by ₱${overBy.toFixed(2)}.\nProceed?`);
        if (!ok) return; // cancel update
        const overId = await ensureLabelByName("Over budget", "#ef4444");
        if (overId && !finalEditLabelIds.includes(overId)) finalEditLabelIds.push(overId);
      }
    }   

      await updateExpense(editId, {
        tax_json: taxSnapshot,
        occurred_on: occurredOn,
        amount: amountNum,
        category_id: chosenCategoryId,
        notes: notes || null,
        status,
        contact_id: contactId || null,
        label_ids: finalEditLabelIds,
      });

      if (editFiles.length) {
        await uploadAttachments(editId, editFiles);
      }

      setShowEditModal(false);
      setEditId(null);

      setAmount('');
      setTaxType('NONE');
      setTaxRate(0.03);
      setTaxInclusive(false);
      setWithholdingRate(0);
      setMainCatId('');
      setSubCatId('');
      setNotes('');
      setLabelIds([]);
      setContactId('');
      setStatus('uncleared');
      setEditFiles([]);

      await refresh();
    } catch (err) {
      console.error(err);
      alert(`❌ Failed to update expense: ${err?.message || 'Unknown error'}`);
    }
  }

  async function handleDeleteExpense(id) {
    const row = rows.find(r => r.id === id);
    setConfirmOpen(true);
    confirmRef.current = {
      title: "Delete expense",
      message: "This will delete the expense and its attachments.",
      onConfirm: async () => {
        setConfirmOpen(false);
        try {
          await deleteExpenseDeep(id);             
          setRows(prev => prev.filter(r => r.id !== id));
        } catch (e) {
          notify.error(e?.message || "Failed to delete expense");
        }
      }
    };

  }



  async function handleDeleteLabel(id) {
  if (!confirm('Delete this label permanently? This cannot be undone.')) return;
  try {
    await deleteLabel(id);                   // hard delete in DB
    const next = await listLabels();         // refresh list
    setLabels(next);
    // remove from current selection if it was selected
    setLabelIds(prev => prev.filter(x => next.some(l => l.id === x)));
  } catch (e) {
    alert(`Failed to delete label: ${e?.message || 'Unknown error'}`);
  }
}



  async function openEdit(row) {
    try {
      setEditId(row.id);
      setOccurredOn(String(row.occurred_on).slice(0,10));
      setAmount(String(row.amount));
      setEditOriginalAmount(Number(row.amount || 0));
      setStatus(row.status || 'uncleared');
      setContactId(row.contact_id || '');
      setNotes(row.notes || '');

      if (row.attachments_count != null) setEditAttachmentCount(row.attachments_count);

      
      const t = row.tax_json || null;
        const nextType = t?.type || 'NONE';

        // Parse numbers whether they came back as "0.12" (string) or 0.12 (number)
        const parsedRate = Number(t?.rate);
        const parsedWh   = Number(t?.withholding_rate);

        setTaxType(nextType);
        setTaxRate(
          Number.isFinite(parsedRate)
            ? parsedRate
            : nextType === 'VAT'
              ? 0.12
              : nextType === 'PERCENTAGE_TAX'
                ? 0.03
                : 0
        );
        setTaxInclusive(Boolean(t?.is_inclusive));
        setWithholdingRate(Number.isFinite(parsedWh) ? parsedWh : 0);


      const ids = (row.label_badges || []).map(b => b.id);
      setLabelIds(ids);

      if (row.category_id) {
        const cat = await getCategoryById(row.category_id);
        if (cat?.parent_id) {
          setMainCatId(cat.parent_id);
          const subsNow = await fetchSubcategories(cat.parent_id);
          setSubs(subsNow);
          setSubCatId(cat.id);
        } else {
          setMainCatId(cat?.id || '');
          const subsNow = await fetchSubcategories(cat?.id);
          setSubs(subsNow);
          setSubCatId('');
        }
      } else {
        setMainCatId('');
        setSubCatId('');
        setSubs([]);
      }

      setShowEditModal(true);
    } catch (e) {
      console.error(e);
      alert(`Failed to open editor: ${e?.message || 'Unknown error'}`);
    }
  }

  function onCalendarStartDateChange({ activeStartDate, view }) {
    if (view === 'month') {
      const yymm = formatYYYYMM(activeStartDate);
      setSelectedMonth(yymm);
      const ms = new Date(`${yymm}-01`);
      const me = new Date(ms.getFullYear(), ms.getMonth()+1, 0);
      if (calendarDate < ms || calendarDate > me) setCalendarDate(ms);
    }
  }

// 1) Try inline JSON/array fields on the row itself
function getInlineAttachmentsFromRow(row) {
  return (
    row?.attachments ||
    row?.files ||
    row?.images ||
    row?.docs ||
    row?.attachments_json ||
    []
  );
}

  function handleTaxTypeChange(nextType) {
    setTaxType(nextType);

    // Auto-suggest a sensible default but keep it editable
    if (nextType === 'VAT') {
      setTaxRate(0.12);
    } else if (nextType === 'PERCENTAGE_TAX') {
      setTaxRate(0.03);
    } else if (nextType === 'NONE') {
      // Optional: keep current rate or clear suggestion baseline
      // setTaxRate(0.03);
    }
  }


  function resetAddExpenseForm() {
    setOccurredOn(new Date().toISOString().slice(0,10));
    setAmount('');
    setMainCatId('');
    setSubCatId('');
    setNotes('');
    setLabelIds([]);
    setContactId('');
    setStatus('uncleared');
    setNewFiles([]);
    setExpanded(false);

    // tax defaults
    setTaxType('NONE');
    setTaxRate(0.03);
    setTaxInclusive(false);
    setWithholdingRate(0);
  }

  function closeEditModalAndReset() {
    setShowEditModal(false);
    setEditId(null);
    setEditFiles([]);
    setEditAttachmentCount(0);

    // Critical: clear the shared form state so Add opens clean
    resetAddExpenseForm();
  }


  const amountNum = Number.parseFloat(String(amount).trim());
  const canSave =
    Number.isFinite(amountNum) &&
    amountNum > 0 &&
    Boolean(occurredOn) &&
    Boolean(subCatId || mainCatId);

  // ======== Render ========
  return (
    <div className="dashboard-container">
      <header className="header-bar">
        <h1 className="header-title">BuiswAIz</h1>
      </header>
      <div className="main-section">
        <aside className="sidebar">
          <div className="nav-section">
            <p className="nav-header">GENERAL</p>
            <ul>
              <li onClick={() => navigate("/Dashboard")}>Dashboard</li>
              <li onClick={() => navigate("/inventory")}>Inventory</li>
              <li onClick={() => navigate("/TablePage")}>Sales</li>
              <li className="active">Expenses</li>
              <li onClick={() => navigate("/assistant")}>AI Assistant</li>
            </ul>
            <p className="nav-header">RELATED</p>
            <ul>
              <li onClick={() => navigate("/supplier")}>Supplier</li>
              <li onClick={() => navigate("/pos")}>Point of Sales</li>
              <li onClick={() => navigate("/PlannedPaymentsPage")}>Planned Payment</li>
            </ul>
          </div>
        </aside>

        <main className="main">
          <div className="top-summary">

            {/* 1) Month-to-date vs Budget (with overspend meter) */}
            <div className="summary-card kpi-card">
              <div className="kpi-head">
                <h3>Month-to-date</h3>
                <span className={`risk-pill risk--${risk}`}>
                  {risk === "over" ? "Over Budget" :
                  risk === "high" ? "At Risk" :
                  risk === "ok" ? "On Track" : "No Budget"}
                </span>
              </div>

              <div className="kpi-main">₱{spent.toFixed(2)}</div>
              <div className={`meter meter--${risk}`}>
                <div className="meter__bar" style={{ width: `${spendPct * 100}%` }} />
              </div>

              <div className="stat-row">
                <div className="stat">
                  <div className="stat__label">Budget</div>
                  <div className="stat__value">₱{Number(budget || 0).toFixed(2)}</div>
                </div>
                <div className="stat">
                  <div className="stat__label">{overspend > 0 ? "Over by" : "Remaining"}</div>
                  <div className={`stat__value ${overspend > 0 ? "text-danger" : "text-ok"}`}>
                    {overspend > 0 ? `₱${overspend.toFixed(2)}` : `₱${remaining.toFixed(2)}`}
                  </div>
                </div>
                <div className="stat">
                  <div className="stat__label">% of Budget</div>
                  <div className="stat__value">
                    {budget > 0 ? ((spent / budget) * 100).toFixed(0) + "%" : "—"}
                  </div>
                </div>
              </div>
            </div>

            {/* 3) Today vs. Daily Budget */}
            <div className="summary-card kpi-card">
              <div className="kpi-head">
                <h3>Today</h3>
                <span className="subtle">vs daily allowance</span>
              </div>
              <div className="kpi-main">₱{dailyTotal.toFixed(2)}</div>
              <div className="meter meter--ok">
                <div className="meter__bar" style={{ width: `${dailyPct * 100}%` }} />
              </div>
              <div className="stat-row">
                <div className="stat">
                  <div className="stat__label">Daily Budget</div>
                  <div className="stat__value">₱{dailyBudget.toFixed(2)}</div>
                </div>
                <div className="stat">
                  <div className="stat__label">{dailyTotal > dailyBudget ? "Over today" : "Left today"}</div>
                  <div className={`stat__value ${dailyTotal > dailyBudget ? "text-danger" : "text-ok"}`}>
                    ₱{Math.abs(dailyBudget - dailyTotal).toFixed(2)}
                  </div>
                </div>
              </div>
            </div>

            {/* 4) Top Category This Month */}
            <div className="summary-card kpi-card">
              <div className="kpi-head">
                <h3>Top Category</h3>
                <span className="subtle">{selectedMonth}</span>
              </div>
              <div className="kpi-main">{topCat.name}</div>
              <div className="stat-row">
                <div className="stat">
                  <div className="stat__label">Spent</div>
                  <div className="stat__value">₱{topCat.amt.toFixed(2)}</div>
                </div>
                <div className="stat">
                  <div className="stat__label">% of Month</div>
                  <div className="stat__value">
                    {spent > 0 ? ((topCat.amt / spent) * 100).toFixed(0) + "%" : "—"}
                  </div>
                </div>
              </div>
            </div>

          </div>


          {monthlyTotal > budget && (
            <div className="warning-banner">
              ⚠️ You're over your monthly budget by ₱{(monthlyTotal - budget).toFixed(2)}!
            </div>
          )}

          {/* Toolbar */}
          <section className="toolbar">
            <div className="toolbar-left">
              <button className="btn primary" onClick={() =>{resetAddExpenseForm(); setShowAddModal(true);}}>
                + Add Expense
              </button>
            </div>
              <div className="toolbar-right" style={{ display: "flex", gap: 8 }}>
                <BudgetCenter triggerClass="btn primary" triggerLabel="Budget" />
                <ContactsCenter
                  triggerClass="btn primary"
                  triggerLabel="Contacts"
                  onOpenExpense={handleOpenExpenseFromContacts}
                />
                <TaxCenter triggerClass="btn primary" triggerLabel="Tax" />
              </div>

            
          </section>

          {/* Controls */}
          <section className="table-controls">
            <div className="control">
              <label>Month</label>
              <input
                type="month"
                className="control-input"
                value={selectedMonth}
                onChange={(e) => {
                  const v = e.target.value;
                  if (!v) return;
                  setRangeMode('month');
                  setSelectedMonth(v);
                }}
              />
            </div>

            <div className="control">
              <label>Category</label>
              <select
                className="control-input"
                value={selectedCategory}
                onChange={(e) => setSelectedCategory(e.target.value)}
              >
                <option value="all">All categories</option>
                {categories.map((c) => (
                  <option key={c} value={c}>{c}</option>
                ))}
              </select>
            </div>

            <div className="control">
              <label>Sort</label>
              <select
                className="control-input"
                value={sortMode}
                onChange={(e) => setSortMode(e.target.value)}
              >
                <option value="date_desc">Newest first</option>
                <option value="date_asc">Oldest first</option>
                <option value="amount_desc">Amount: High → Low</option>
                <option value="amount_asc">Amount: Low → High</option>
              </select>
            </div>
          </section>

          {/* Table */}
          <div className="Ex-table-scroll-box">
            <table>
              <thead>
                <tr>
                  <th>Date</th>
                  <th>Category</th>
                  <th>Description</th>
                  <th>Labels</th>
                  <th>Amount</th>
                  <th>Attach</th>
                  <th className="col-actions">Actions</th>
                </tr>
              </thead>
              <tbody>
                {visibleExpenses.map((r) => (
                  <tr
                    key={r.id}
                    className="clickable-row"
                    onClick={() => handleOpenExpenseFromContacts(r.id)}
                    tabIndex={0}
                    onKeyDown={(e) => { if (e.key === 'Enter') handleOpenExpenseFromContacts(r.id); }}
                    role="button"
                    aria-label="Edit expense"
                  >
                    <td>{r.occurred_on}</td>
                    <td>{r.category_path ?? '—'}</td>
                    <td>{r.notes ?? ''}</td>
                    <td>
                      <div style={{display:'flex', gap:6, flexWrap:'wrap', justifyContent:'center'}}>
                        {(r.label_badges ?? []).map((b) => (
                          <span key={b.id} className="px-2 py-0.5 rounded-full" style={{ border: '1px solid '+b.color }}>
                            <span style={{ display:'inline-block', width:8, height:8, borderRadius:999, background:b.color, marginRight:6 }} />
                            {b.name}
                          </span>
                        ))}
                      </div>
                    </td>
                    <td>₱{Number(r.amount).toFixed(2)}</td>
                    <td>📎 {r.attachments_count ?? 0}</td>
                    <td className="col-actions">
                      <div
                        className="table-actions"
                        onClick={(e) => e.stopPropagation()}   // prevent row onClick
                      >
                      <button
                        type="button"
                        className="btn xs"
                         onClick={(e) => {
                         e.preventDefault();
                         e.stopPropagation();
                         setSelectedId(r.id); // open the original AttachmentsPanel
                       }}
                      >
                        View attachments
                      </button>


                        <button
                          className="btn xs danger"
                          onClick={(e) => {
                            e.stopPropagation();
                            handleDeleteExpense(r.id);                   // use r
                          }}
                          title="Delete expense"
                        >
                          Delete
                        </button>
                      </div>
                    </td>

                  </tr>
                ))}
                {visibleExpenses.length === 0 && (
                  <tr>
                    <td colSpan={7} style={{ padding: '20px', color: '#64748b' }}>
                      No expenses in the selected range.
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>


          {/* Chart + Calendar */}
          <div className="chart-and-calendar">
            <div className="chart-container">
              <h3>Yearly Expenses Trend</h3>
              <ResponsiveContainer width="100%" height={300}>
                <LineChart data={chartData}>
                  <XAxis dataKey="month" />
                  <YAxis />
                  <Tooltip />
                  <CartesianGrid stroke="#eee" strokeDasharray="5 5" />
                  <Line type="monotone" dataKey="total" stroke="#3b82f6" strokeWidth={2} />
                </LineChart>
              </ResponsiveContainer>
            </div>
            <div className="calendar-container">
              <h3>Calendar</h3>
              <Calendar
                value={calendarDate}
                onChange={setCalendarDate}
                minDetail="month"
                maxDetail="month"
                activeStartDate={monthStart}
                onActiveStartDateChange={onCalendarStartDateChange}
                minDate={monthStart}
                maxDate={monthEnd}
                tileDisabled={({ date, view }) =>
                  view === 'month' &&
                  (date.getMonth() !== monthStart.getMonth() ||
                  date.getFullYear() !== monthStart.getFullYear())
                }
              />
            </div>
          </div>
        </main>
      </div>

        
      <ConfirmDeleteModal
      isOpen={confirmOpen}
      message={confirmRef.current.message}
      onCancel={() => setConfirmOpen(false)}
      onConfirm={confirmRef.current.onConfirm}
      />




      {/* Add Expense modal */}
      {showAddModal && (
        <div
          className="modal-overlay fancy"
          role="dialog"
          aria-modal="true"
          aria-label="Add Expense"
          tabIndex={-1}
          onMouseDown={(e) => { if (e.target === e.currentTarget) setShowAddModal(false); }}
          onKeyDown={(e) => { if (e.key === 'Escape') setShowAddModal(false); }}
        >
          <form className="modal sheet animate-in" onSubmit={handleSaveExpense}>
            <div className="modal-header">
              <h2 className="modal-title">Add Expense</h2>
              <button type="button" className="icon-btn" aria-label="Close" onClick={() => setShowAddModal(false)}>✕</button>
            </div>

            <div className="modal-body">
              <div className="accent-bar" />
              <div className="fields-grid">
                <hr className="hr-ghost" />
                <div className="field">
                  <label>Date</label>
                  <input type="date" value={occurredOn} onChange={(e) => setOccurredOn(e.target.value)} required />
                </div>
                <div className="field">
                  <label>Main category</label>
                  <select value={mainCatId} onChange={(e) => setMainCatId(e.target.value)}>
                    <option value="">—</option>
                    {mains.map((m) => <option key={m.id} value={m.id}>{m.name}</option>)}
                  </select>
                </div>
                <div className="field">
                  <label>Subcategory</label>
                  <select value={subCatId} onChange={(e) => setSubCatId(e.target.value)} disabled={!subs.length}>
                    <option value="">—</option>
                    {subs.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
                  </select>
                </div>
                {/* Amount (emphasized) */}
                <div className="field amount-field field--full">
                  <label>Amount</label>
                  <div className="money-group">
                    <div className="money-prefix">₱</div>
                    <input
                      className="money-input"
                      type="number"
                      step="0.01"
                      min="0"
                      placeholder="0.00"
                      value={amount}
                      onChange={(e) => setAmount(e.target.value)}
                      required
                    />
                  </div>
                </div>

              </div>

              <div style={{ display:'flex', justifyContent:'flex-end' }}>
                <button type="button" className="btn outline" onClick={() => setExpanded(v => !v)}>
                  {expanded ? 'Hide details' : 'Add details ▸'}
                </button>
              </div>

              {expanded && (
                <div className="fields-grid" style={{ marginTop: 8 }}>
                  {/* 1) Description */}
                  <div className="section-title" style={{ gridColumn: '1 / -1' }}>Description</div>
                  <div className="field" style={{ gridColumn: '1 / -1' }}>
                    <input
                      placeholder="Add a description…"
                      value={notes}
                      onChange={(e) => setNotes(e.target.value)}
                    />
                  </div>

                  <hr className="section-divider" style={{ gridColumn: '1 / -1' }} />

                  {/* 2) Tax */}
                  <div className="fieldset-card">
                    <div className="section-head">
                      <div className="title">Tax</div>
                      <p className="hint">Configure how amount is treated</p>
                    </div>

                    {/* KPI row */}
                    {(() => {
                      const { net, tax, gross } = calcTax({
                        amount: Number(amount || 0),
                        type: taxType, rate: taxRate, isInclusive: taxInclusive, withholdingRate,
                      });
                      return (
                        <div className="kpi-row" style={{ marginTop: 2 }}>
                          <div className="kpi kpi--net"><span className="kpi__label">Net</span><span className="kpi__value">₱{(net ?? 0).toFixed(2)}</span></div>
                          <div className="kpi kpi--tax"><span className="kpi__label">Tax</span><span className="kpi__value">₱{(tax ?? 0).toFixed(2)}</span></div>
                          <div className="kpi kpi--gross"><span className="kpi__label">Gross</span><span className="kpi__value">₱{(gross ?? 0).toFixed(2)}</span></div>
                        </div>
                      );
                    })()}

                    {/* Tax controls (your existing 4 fields) */}
                    <div className="fields-grid" style={{ marginTop: 8 }}>
                      {/* Type / Rate / Inclusive / Withholding — unchanged */}
                      {/* ... paste your existing 4 fields here ... */}
                    </div>
                  </div>


                  {/* Tax controls */}
                  <div className="field">
                    <label>Type</label>
                    <select value={taxType} onChange={(e) => handleTaxTypeChange(e.target.value)}>
                      <option value="NONE">None</option>
                      <option value="PERCENTAGE_TAX">Percentage Tax</option>
                      <option value="VAT">VAT</option>
                    </select>
                  </div>

                  <div className="field">
                    <label>Rate</label>
                    <input
                      type="number"
                      step="0.0001"
                      placeholder={taxType === 'VAT' ? '0.12' : '0.03'}
                      disabled={taxType === 'NONE'}
                      value={taxType === 'NONE' ? '' : taxRate}
                      onChange={(e) => setTaxRate(Number(e.target.value || 0))}
                    />
                  </div>

                  <div className="field">
                    <label>
                      <input
                        type="checkbox"
                        disabled={taxType === 'NONE'}
                        checked={taxType === 'NONE' ? false : taxInclusive}
                        onChange={(e) => setTaxInclusive(e.target.checked)}
                      />
                      &nbsp;Inclusive
                    </label>
                    <div className="hint">If checked, the Amount includes the tax.</div>
                  </div>

                  <div className="field">
                    <label>Withholding Rate</label>
                    <input
                      type="number"
                      step="0.0001"
                      placeholder="0.01"
                      disabled={taxType === 'NONE'}
                      value={taxType === 'NONE' ? '' : withholdingRate}
                      onChange={(e) => setWithholdingRate(Number(e.target.value || 0))}
                    />
                    <div className="hint">Optional. e.g. 0.01 means 1% withholding.</div>
                  </div>

                  <hr className="section-divider" style={{ gridColumn: '1 / -1' }} />

                  {/* 3) Contacts & Labels */}
                  <div className="section-title" style={{ gridColumn: '1 / -1' }}>Contacts &amp; Labels</div>

                  <div className="field">
                    <label>Contact</label>
                    <select value={contactId} onChange={(e) => setContactId(e.target.value)}>
                      <option value="">—</option>
                      {contacts.map((c) => (
                        <option key={c.id} value={c.id}>{c.name}</option>
                      ))}
                    </select>
                    <div style={{ marginTop: 6 }}>
                      <QuickContactInline
                        onCreated={(c) => {
                          setContacts((prev) => [...prev, c]);
                          setContactId(c.id);
                        }}
                      />
                    </div>
                  </div>

                  <div className="field" style={{ gridColumn: '1 / -1' }}>
                    <label>Labels</label>
                    <div className="label-cloud">
                      {labels.map((l) => {
                        const checked = labelIds.includes(l.id);
                        return (
                          <div className={`chip ${checked ? 'chip--selected' : ''}`} key={l.id} title={l.name}>
                            <button
                              type="button"
                              className="chip__main"
                              onClick={() =>
                                setLabelIds((prev) => (checked ? prev.filter((id) => id !== l.id) : [...prev, l.id]))
                              }
                            >
                              <span className="chip__dot" style={{ background: l.color || '#64748b' }} />
                              <span className="chip__text">{l.name}</span>
                            </button>
                            <button
                              type="button"
                              className="chip__delete"
                              aria-label={`Delete ${l.name}`}
                              title="Delete label"
                              onClick={(e) => { e.stopPropagation(); handleDeleteLabel(l.id); }}
                            >
                              ×
                            </button>
                          </div>
                        );
                      })}
                    </div>

                    <div style={{ marginTop: 8 }}>
                      <QuickLabelInline
                        onCreated={(lbl) => {
                          setLabels((prev) => [...prev, lbl]);
                        }}
                      />
                    </div>
                  </div>

                  <hr className="section-divider" style={{ gridColumn: '1 / -1' }} />

         
                  {/* 4) Attachments */}
                  <div className="fieldset-card">
                    <div className="section-head">
                      <div className="title">Attachments</div>
                      <p className="hint">Upload JPEG/PNG receipts</p>
                    </div>

                    <div className="field" style={{ gridColumn:'1 / -1' }}>
                      <input type="file" accept="image/jpeg,image/png" multiple
                            onChange={(e) => setNewFiles(Array.from(e.target.files || []))} />
                    </div>

                    {!!newFiles.length && (
                      <div className="attach-grid" style={{ marginTop: 8 }}>
                        {newFiles.map((f, i) => (
                          <div key={i} className="attach-card">
                            <div className="attach-thumb">
                              <img src={URL.createObjectURL(f)} alt={f.name}
                                  style={{ maxWidth:'100%', maxHeight:'100%', objectFit:'cover' }}
                                  onLoad={(e) => URL.revokeObjectURL(e.currentTarget.src)} />
                            </div>
                            <div style={{ fontSize: 12, marginTop: 6, color: '#475569' }}>
                              {f.name.length > 20 ? f.name.slice(0, 17) + '…' : f.name}
                            </div>
                            <div style={{ fontSize: 11, color: '#64748b' }}>
                              {(f.size/1024).toFixed(1)} KB
                            </div>
                          </div>
                        ))}
                      </div>
                    )}
                  </div>

                </div>
              )}
              
            </div>

            <div className="modal-footer">
              <button type="button" className="btn secondary" onClick={() => setShowAddModal(false)}>
                Cancel
              </button>
              <button type="submit" className="btn primary" disabled={!canSave}>
                Add Expense
              </button>
            </div>

          </form>
        </div>
      )}

      {/* Edit Expense modal */}
      {showEditModal && (
        <div
          className="modal-overlay fancy"
          role="dialog"
          aria-modal="true"
          aria-label="Edit Expense"
          tabIndex={-1}
          onMouseDown={(e) => { if (e.target === e.currentTarget) setShowEditModal(false); }}
          onKeyDown={(e) => { if (e.key === 'Escape') setShowEditModal(false); }}
        >
          <form className="modal sheet animate-in" onSubmit={handleUpdateExpense}>
            <div className="modal-header">
              <h2 className="modal-title">Edit Expense</h2>
              <button type="button" className="icon-btn" aria-label="Close" onClick={closeEditModalAndReset}>✕</button>
            </div>

            <div className="modal-body">
              <div className="accent-bar" />
              <div className="fields-grid">
                <hr className="hr-ghost" />
                <div className="field">
                  <label>Date</label>
                  <input type="date" value={occurredOn} onChange={(e) => setOccurredOn(e.target.value)} required />
                </div>
                <div className="field">
                  <label>Main category</label>
                  <select value={mainCatId} onChange={async (e) => {
                    const val = e.target.value;
                    setMainCatId(val);
                    setSubCatId('');
                    if (val) {
                      const subsNow = await fetchSubcategories(val);
                      setSubs(subsNow);
                    } else {
                      setSubs([]);
                    }
                  }}>
                    <option value="">—</option>
                    {mains.map((m) => <option key={m.id} value={m.id}>{m.name}</option>)}
                  </select>
                </div>
                <div className="field">
                  <label>Subcategory</label>
                  <select value={subCatId} onChange={(e) => setSubCatId(e.target.value)} disabled={!subs.length}>
                    <option value="">—</option>
                    {subs.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
                  </select>
                </div>
                {/* Amount (emphasized) */}
                <div className="field amount-field field--full">
                  <label>Amount</label>
                  <div className="money-group">
                    <div className="money-prefix">₱</div>
                    <input
                      className="money-input"
                      type="number"
                      step="0.01"
                      min="0"
                      placeholder="0.00"
                      value={amount}
                      onChange={(e) => setAmount(e.target.value)}
                      required
                    />
                  </div>
                </div>

              </div>

              <div className="fields-grid" style={{ marginTop: 8 }}>
                {/* 1) Description */}
                <div className="section-title" style={{ gridColumn: '1 / -1' }}>Description</div>
                <div className="field" style={{ gridColumn: '1 / -1' }}>
                  <input
                    placeholder="Add a note…"
                    value={notes}
                    onChange={(e) => setNotes(e.target.value)}
                  />
                </div>

                <hr className="section-divider" style={{ gridColumn: '1 / -1' }} />

                {/* 2) Tax */}
                <div className="section-title" style={{ gridColumn: '1 / -1' }}>Tax</div>

                {/* KPI row already shown under Amount; you can keep a second KPI row here if you like.
                    If you want it here too, uncomment the block below.
                */}
                {true && (() => {
                  const { net, tax, gross } = calcTax({
                    amount: Number(amount || 0),
                    type: taxType,
                    rate: taxRate,
                    isInclusive: taxInclusive,
                    withholdingRate,
                  });
                  return (
                    <div className="kpi-row" style={{ gridColumn: '1 / -1', marginTop: 2 }}>
                      <div className="kpi kpi--net">
                        <span className="kpi__label">Net</span>
                        <span className="kpi__value">₱{(net ?? 0).toFixed(2)}</span>
                      </div>
                      <div className="kpi kpi--tax">
                        <span className="kpi__label">Tax</span>
                        <span className="kpi__value">₱{(tax ?? 0).toFixed(2)}</span>
                      </div>
                      <div className="kpi kpi--gross">
                        <span className="kpi__label">Gross</span>
                        <span className="kpi__value">₱{(gross ?? 0).toFixed(2)}</span>
                      </div>
                    </div>
                  );
                })()}

                {/* Tax controls */}
                <div className="field">
                  <label>Type</label>
                  <select value={taxType} onChange={(e) => handleTaxTypeChange(e.target.value)}>
                    <option value="NONE">None</option>
                    <option value="PERCENTAGE_TAX">Percentage Tax</option>
                    <option value="VAT">VAT</option>
                  </select>
                </div>

                <div className="field">
                  <label>Rate</label>
                  <input
                    type="number"
                    step="0.0001"
                    placeholder={taxType === 'VAT' ? '0.12' : '0.03'}
                    disabled={taxType === 'NONE'}
                    value={taxType === 'NONE' ? '' : taxRate}
                    onChange={(e) => setTaxRate(Number(e.target.value || 0))}
                  />
                </div>

                <div className="field">
                  <label>
                    <input
                      type="checkbox"
                      disabled={taxType === 'NONE'}
                      checked={taxType === 'NONE' ? false : taxInclusive}
                      onChange={(e) => setTaxInclusive(e.target.checked)}
                    />
                    &nbsp;Inclusive
                  </label>
                  <div className="hint">If checked, the Amount includes the tax.</div>
                </div>

                <div className="field">
                  <label>Withholding Rate</label>
                  <input
                    type="number"
                    step="0.0001"
                    placeholder="0.01"
                    disabled={taxType === 'NONE'}
                    value={taxType === 'NONE' ? '' : withholdingRate}
                    onChange={(e) => setWithholdingRate(Number(e.target.value || 0))}
                  />
                  <div className="hint">Optional. e.g. 0.01 means 1% withholding.</div>
                </div>

                <hr className="section-divider" style={{ gridColumn: '1 / -1' }} />

                {/* 3) Contacts & Labels */}
                <div className="section-title" style={{ gridColumn: '1 / -1' }}>Contacts &amp; Labels</div>

                <div className="field">
                  <label>Contact</label>
                  <select value={contactId} onChange={(e) => setContactId(e.target.value)}>
                    <option value="">—</option>
                    {contacts.map((c) => (
                      <option key={c.id} value={c.id}>{c.name}</option>
                    ))}
                  </select>
                </div>

                <div className="field" style={{ gridColumn:'1 / -1' }}>
                  <label>Labels</label>
                  <div className="label-cloud">
                    {labels.map((l) => {
                      const checked = labelIds.includes(l.id);
                      return (
                        <button
                          type="button"
                          key={l.id}
                          className={`chip ${checked ? 'chip--selected' : ''}`}
                          onClick={() => setLabelIds(prev => checked ? prev.filter(id => id !== l.id) : [...prev, l.id])}
                          title={l.name}
                        >
                          <span className="chip__dot" style={{ background: l.color || '#64748b' }} />
                          <span className="chip__text">{l.name}</span>
                        </button>
                      );
                    })}
                  </div>
                </div>

                <hr className="section-divider" style={{ gridColumn: '1 / -1' }} />

                {/* 4) Attachments */}
                <div className="section-title" style={{ gridColumn: '1 / -1' }}>Attachments</div>
                <div className="field" style={{ gridColumn:'1 / -1' }}>
                  <label>Attachments (JPEG/PNG)</label>
                  <input
                    type="file"
                    accept="image/jpeg,image/png"
                    multiple
                    onChange={(e) => setEditFiles(Array.from(e.target.files || []))}
                  />
                  {!!editFiles.length && (
                    <div style={{ display:'flex', gap:12, flexWrap:'wrap', marginTop:8 }}>
                      {editFiles.map((f, i) => (
                        <div key={i} className="border rounded-xl p-2" style={{ width: 140 }}>
                          <div
                            style={{
                              width: '100%', height: 90, overflow: 'hidden',
                              borderRadius: 12, background: '#f1f5f9', display: 'flex',
                              alignItems: 'center', justifyContent: 'center'
                            }}
                          >
                            <img
                              src={URL.createObjectURL(f)}
                              alt={f.name}
                              style={{ maxWidth:'100%', maxHeight:'100%', objectFit:'cover' }}
                              onLoad={(e) => URL.revokeObjectURL(e.currentTarget.src)}
                            />
                          </div>
                          <div style={{ fontSize: 12, marginTop: 6, color: '#475569' }}>
                            {f.name.length > 20 ? f.name.slice(0, 17) + '…' : f.name}
                          </div>
                          <div style={{ fontSize: 11, color: '#64748b' }}>
                            {(f.size/1024).toFixed(1)} KB
                          </div>
                        </div>
                      ))}
                    </div>
                  )}
                </div>
              </div>

            </div>

            <div className="modal-footer">
              <button type="button" className="btn secondary" onClick={closeEditModalAndReset}>Cancel</button>
              <button type="submit" className="btn primary">Save changes</button>
            </div>
          </form>
        </div>
      )}

      {/* Attachments panel */}
      {selectedId && (
        <div className="main" style={{ paddingTop: 0 }}>
          <section className="rounded-2xl border p-4">
              <AttachmentsPanel
              expenseId={selectedId}
              onClose={() => setSelectedId(null)}
            />

          </section>
        </div>
      )}
    </div>
  );


};

// Inline “quick contact”
function QuickContactInline({ onCreated }) {
  const [open, setOpen] = useState(false);
  const [name, setName]   = useState('');
  const [email, setEmail] = useState('');
  const [phone, setPhone] = useState('');
  const [saving, setSaving] = useState(false);

  const emailValid = !email || /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email);
  const canSave = name.trim() && emailValid && !saving;

  async function submit() {
    if (!canSave) return;
    setSaving(true);
    try {
      const c = await createContact({ name: name.trim(), email: email.trim(), phone: phone.trim() });
      onCreated(c);
      setOpen(false);
      setName(''); setEmail(''); setPhone('');
    } catch (e) {
      alert(`Failed to create contact: ${e?.message || 'Unknown error'}`);
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="inline-add" style={{ marginTop: 6 }}>
      {!open && (
        <button type="button" className="btn link" onClick={() => setOpen(true)}>
          + New contact
        </button>
      )}
      {open && (
        <div className="inline-card">
          <div className="fields-grid">
            <div className="field">
              <label>Name</label>
              <input value={name} onChange={(e)=>setName(e.target.value)} placeholder="Contact name" />
            </div>
            <div className="field">
              <label>Email</label>
              <input value={email} onChange={(e)=>setEmail(e.target.value)} placeholder="email@example.com" type="email" />
              {!emailValid && <div className="hint error">Please enter a valid email</div>}
            </div>
            <div className="field">
              <label>Phone</label>
              <input value={phone} onChange={(e)=>setPhone(e.target.value)} placeholder="+63…" />
              <div className="hint">Optional</div>
            </div>
            <div className="field" style={{ gridColumn:'1 / -1' }}>
              <div className="actions">
                <button type="button" className="btn primary" disabled={!canSave} onClick={submit}>
                  Save contact
                </button>
                <button type="button" className="btn secondary" onClick={()=>setOpen(false)}>
                  Cancel
                </button>
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}


function QuickLabelInline({ onCreated }) {
  const [open, setOpen] = useState(false);
  const [name, setName] = useState('');
  const [color, setColor] = useState('#6366f1');
  const [saving, setSaving] = useState(false);

  async function submit() {
    if (!name.trim()) return;
    setSaving(true);
    try {
      const lbl = await createLabel({ name: name.trim(), color });
      onCreated?.(lbl);
      setOpen(false);
      setName('');
      setColor('#6366f1');
    } catch (e) {
      console.error(e);
      alert(`Failed to create label: ${e?.message || 'Unknown error'}`);
    } finally {
      setSaving(false);
    }
  }

  if (!open) {
    return (
      <button type="button" className="btn xs outline" onClick={() => setOpen(true)}>
        + New label
      </button>
    );
  }

  return (
    <div className="fields-grid" style={{ marginTop: 4, width: '100%', maxWidth: 520 }}>
      <div className="field">
        <label>Name</label>
        <input
          placeholder="e.g. Tax, Urgent, Reimbursable"
          value={name}
          onChange={(e) => setName(e.target.value)}
        />
      </div>
      <div className="field">
        <label>Color</label>
        <input type="color" value={color} onChange={(e) => setColor(e.target.value)} style={{ height: 38, padding: 4 }} />
      </div>
      <div className="field" style={{ gridColumn:'1 / -1' }}>
        <button type="button" className="btn primary" disabled={saving || !name.trim()} onClick={submit}>
          Save label
        </button>
        <button type="button" className="btn secondary" style={{ marginLeft: 8 }} onClick={() => setOpen(false)}>
          Cancel
        </button>
      </div>
    </div>
  );



}

export default ExpenseDashboard;
