// src/api/expenses.js
import { supabase } from '../supabase';


// Your table uses "occured_on" (one "r")
const DATE_COL = 'occurred_on';

export async function listExpensesByMonth(yyyy, mm) {
  const { data: u } = await supabase.auth.getUser();
  if (!u?.user) return [];

  const from = new Date(Date.UTC(yyyy, mm - 1, 1)).toISOString().slice(0, 10);
  const to   = new Date(Date.UTC(yyyy, mm, 1)).toISOString().slice(0, 10);

  const baseSelect = `id, ${DATE_COL}, amount, notes, status, category_id, contact_id`;

  // Expenses for this user + month
    const baseRes = await supabase
    .from('expenses')
    .select(baseSelect)
    .gte(DATE_COL, from)
    .lt(DATE_COL, to)
    .order(DATE_COL, { ascending: false });

  if (baseRes.error) {
    console.error('listExpensesByMonth: base query failed:', baseRes.error);
    throw baseRes.error;
  }
  const rows = baseRes.data ?? [];
  if (!rows.length) return [];

  const ids = rows.map(r => r.id);

  // Parallel lookups
  const [labelsJoinRes, catsRes, consRes, attsRes] = await Promise.all([
    supabase
      .from('expense_labels')
      .select('expense_id, labels:label_id(id,name,color)')
      .in('expense_id', ids),

    supabase.from('categories').select('id,name,parent_id'),

    supabase.from('contacts').select('id,name'),

    // Fetch attachments and count in JS (no .group() in supabase-js)
    supabase
      .from('attachments')
      .select('id, expense_id')
      .in('expense_id', ids),
  ]);

  if (labelsJoinRes.error) throw labelsJoinRes.error;
  if (catsRes.error) throw catsRes.error;
  if (consRes.error) throw consRes.error;
  if (attsRes.error) throw attsRes.error;

  const labelsJoin = labelsJoinRes.data ?? [];
  const cats  = catsRes.data ?? [];
  const cons  = consRes.data ?? [];
  const atts  = attsRes.data ?? [];

  const catById = new Map(cats.map(c => [c.id, c]));
  const conById = new Map(cons.map(c => [c.id, c]));

  const lblByExp = new Map();
  labelsJoin.forEach(row => {
    const list = lblByExp.get(row.expense_id) ?? [];
    if (row.labels) list.push(row.labels);
    lblByExp.set(row.expense_id, list);
  });

  // Count attachments per expense
  const attCount = new Map();
  atts.forEach(a => {
    attCount.set(a.expense_id, (attCount.get(a.expense_id) || 0) + 1);
  });

  const path = (catId) => {
    if (!catId) return null;
    const c = catById.get(catId);
    if (!c) return null;
    const p = c.parent_id ? catById.get(c.parent_id) : null;
    return p ? `${p.name} / ${c.name}` : c.name;
  };

  // Normalize date field to "occurred_on" for the UI
  return rows.map(r => ({
    ...r,
    occurred_on: r[DATE_COL],
    category_path: path(r.category_id),
    contact_name: r.contact_id ? (conById.get(r.contact_id)?.name ?? null) : null,
    label_badges: lblByExp.get(r.id) ?? [],
    attachments_count: attCount.get(r.id) ?? 0,
  }));
}

export async function createExpense(input) {
  const { data: u } = await supabase.auth.getUser();
  if (!u?.user) throw new Error('Not authenticated');

  const payload = {
    user_id: u.user.id,
    [DATE_COL]: input.occurred_on,  // write into actual column
    amount: input.amount,
    category_id: input.category_id ?? null,
    notes: input.notes ?? null,
    status: input.status ?? 'uncleared',
    contact_id: input.contact_id ?? null,
    tax_json: input.tax_json ?? null,

  };

  const ins = await supabase.from('expenses').insert(payload).select('*').single();
  if (ins.error) {
    console.error('createExpense failed:', ins.error);
    throw ins.error;
  }
  const exp = ins.data;

  if (input.label_ids?.length) {
    const joinRows = input.label_ids.map(label_id => ({ expense_id: exp.id, label_id }));
    const link = await supabase.from('expense_labels').insert(joinRows);
    if (link.error) {
      console.error('createExpense: label link failed:', link.error);
      throw link.error;
    }
  }

  return exp;
}


export async function updateExpense(expenseId, input) {
  const { data: u } = await supabase.auth.getUser();
  if (!u?.user) throw new Error('Not authenticated');

  const payload = {
    // user_id stays as-is (don’t allow changing owner)
    [DATE_COL]: input.occurred_on,                // date
    amount: input.amount,
    category_id: input.category_id ?? null,
    notes: input.notes ?? null,
    status: input.status ?? 'uncleared',
    contact_id: input.contact_id ?? null,
    tax_json: input.tax_json ?? null,
  };

  const upd = await supabase
    .from('expenses')
    .update(payload)
    .eq('id', expenseId)               
    .select('*')
    .single();

  if (upd.error) {
    console.error('updateExpense failed:', upd.error);
    throw upd.error;
  }

  // labels (optional): replace set if provided
  if (Array.isArray(input.label_ids)) {
    const del = await supabase.from('expense_labels').delete().eq('expense_id', expenseId);
    if (del.error) throw del.error;
    if (input.label_ids.length) {
      const rows = input.label_ids.map(id => ({ expense_id: expenseId, label_id: id }));
      const ins = await supabase.from('expense_labels').insert(rows);
      if (ins.error) throw ins.error;
    }
  }

  return upd.data;
}


// NEW: fetch between arbitrary dates (inclusive start, exclusive end)
export async function listExpensesBetween(startISO, endISO) {
  const { data: u } = await supabase.auth.getUser();
  if (!u?.user) return [];

  const baseSelect = `id, ${DATE_COL}, amount, notes, status, category_id, contact_id`;

  const baseRes = await supabase
    .from('expenses')
    .select(baseSelect)
    .gte(DATE_COL, startISO)
    .lt(DATE_COL, endISO)
    .order(DATE_COL, { ascending: false });

  if (baseRes.error) throw baseRes.error;
  const rows = baseRes.data ?? [];
  if (!rows.length) return [];

  const ids = rows.map(r => r.id);

  const [labelsJoinRes, catsRes, consRes, attsRes] = await Promise.all([
    supabase.from('expense_labels').select('expense_id, labels:label_id(id,name,color)').in('expense_id', ids),
    supabase.from('categories').select('id,name,parent_id'),
    supabase.from('contacts').select('id,name'),
    supabase.from('attachments').select('id, expense_id').in('expense_id', ids),
  ]);
  if (labelsJoinRes.error) throw labelsJoinRes.error;
  if (catsRes.error) throw catsRes.error;
  if (consRes.error) throw consRes.error;
  if (attsRes.error) throw attsRes.error;

  const cats = catsRes.data ?? [];
  const cons = consRes.data ?? [];
  const atts = attsRes.data ?? [];
  const catById = new Map(cats.map(c => [c.id, c]));
  const conById = new Map(cons.map(c => [c.id, c]));
  const lblByExp = new Map();
  (labelsJoinRes.data ?? []).forEach(row => {
    const list = lblByExp.get(row.expense_id) ?? [];
    if (row.labels) list.push(row.labels);
    lblByExp.set(row.expense_id, list);
  });
  const attCount = new Map();
  atts.forEach(a => attCount.set(a.expense_id, (attCount.get(a.expense_id) || 0) + 1));

  const path = (catId) => {
    if (!catId) return null;
    const c = catById.get(catId);
    if (!c) return null;
    const p = c.parent_id ? catById.get(c.parent_id) : null;
    return p ? `${p.name} / ${c.name}` : c.name;
  };

  return rows.map(r => ({
    ...r,
    occurred_on: r[DATE_COL],
    category_path: path(r.category_id),
    contact_name: r.contact_id ? (conById.get(r.contact_id)?.name ?? null) : null,
    label_badges: lblByExp.get(r.id) ?? [],
    attachments_count: attCount.get(r.id) ?? 0,
  }));
}

// NEW: convenience for a whole calendar year
export async function listExpensesByYear(year) {
  const start = new Date(Date.UTC(year, 0, 1)).toISOString().slice(0, 10);
  const end   = new Date(Date.UTC(year + 1, 0, 1)).toISOString().slice(0, 10);
  return listExpensesBetween(start, end);
}

// NEW: delete an expense (also clears labels and attachments metadata first)
export async function deleteExpense(expenseId) {
  // best-effort cleanup: joins first, then attachments meta, then expense
  const delLabels = await supabase.from('expense_labels').delete().eq('expense_id', expenseId);
  if (delLabels.error) console.warn('deleteExpense: label cleanup warning:', delLabels.error);

  const delAtts = await supabase.from('attachments').delete().eq('expense_id', expenseId);
  if (delAtts.error) console.warn('deleteExpense: attachments cleanup warning:', delAtts.error);

  const delExp = await supabase.from('expenses').delete().eq('id', expenseId);
  if (delExp.error) throw delExp.error;

  // NOTE: If files are stored in a storage bucket, we can wire deletion there too.
  return true;
}
