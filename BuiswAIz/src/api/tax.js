// src/api/tax.js
import { supabase } from '../supabase';

const DATE_COL = 'occurred_on';

/**
 * Fetch expenses between [startISO, endISO) that have tax_json.
 * Returns rows with id, occurred_on, amount, category_path, contact_name, notes, tax_json.
 * 
 * 
 * 
 */


export async function listAllExpensesBetween(start, end) {
  // If you have the view:
  // return supabase.from("expense_tax_export_v")
  //   .select("*")
  //   .gte("occurred_on", start)
  //   .lt("occurred_on", end)
  //   .order("occurred_on", { ascending: false })
  //   .then(({ data, error }) => { if (error) throw error; return data; });

  // If you don’t have the view, fallback to expenses (ensure LEFT JOINs server-side)
  const { data, error } = await supabase
    .from("expenses") // must include rows with null tax_json
    .select("id, occurred_on, amount, notes, category_path, contact_name, tax_json, tax_type, tax_rate, tax_is_inclusive, tax_net, tax_tax, tax_gross, tax_withholding")
    .gte("occurred_on", start)
    .lt("occurred_on", end)
    .order("occurred_on", { ascending: false });

  if (error) throw error;
  return data;
}

export async function listTaxedExpensesBetween(startISO, endISO) {
  const { data: u } = await supabase.auth.getUser();
  if (!u?.user) return [];

  const baseRes = await supabase
    .from('expenses')
    .select(`id, ${DATE_COL}, amount, notes, status, category_id, contact_id, tax_json`)
    .gte(DATE_COL, startISO)
    .lt(DATE_COL, endISO)
    .order(DATE_COL, { ascending: false });

  if (baseRes.error) throw baseRes.error;
  const rows = baseRes.data ?? [];
  if (!rows.length) return [];

  const catIds = rows.map(r => r.category_id).filter(Boolean);
  const conIds = rows.map(r => r.contact_id).filter(Boolean);

  const [catsRes, consRes] = await Promise.all([
    catIds.length
      ? supabase.from('categories').select('id, name, parent_id').in('id', catIds)
      : Promise.resolve({ data: [] }),
    conIds.length
      ? supabase.from('contacts').select('id, name').in('id', conIds)
      : Promise.resolve({ data: [] }),
  ]);

  if (catsRes.error) throw catsRes.error;
  if (consRes.error) throw consRes.error;

  const cats = catsRes.data ?? [];
  const cons = consRes.data ?? [];
  const catById = new Map(cats.map(c => [c.id, c]));
  const conById = new Map(cons.map(c => [c.id, c]));

  function categoryPath(catId) {
    if (!catId) return null;
    const c = catById.get(catId);
    if (!c) return null;
    const p = c.parent_id ? catById.get(c.parent_id) : null;
    return p ? `${p.name} / ${c.name}` : c.name;
  }

  return rows.map(r => ({
    ...r,
    occurred_on: r[DATE_COL],
    category_path: categoryPath(r.category_id),
    contact_name: r.contact_id ? (conById.get(r.contact_id)?.name ?? null) : null,
  }));
}

export function monthBounds(yyyyMM) {
  const [y, m] = yyyyMM.split('-').map(n => parseInt(n, 10));
  const start = new Date(Date.UTC(y, m - 1, 1)).toISOString().slice(0, 10);
  const end   = new Date(Date.UTC(y, m, 1)).toISOString().slice(0, 10);
  return { start, end };
}

export function yearBounds(y) {
  const start = new Date(Date.UTC(Number(y), 0, 1)).toISOString().slice(0, 10);
  const end   = new Date(Date.UTC(Number(y) + 1, 0, 1)).toISOString().slice(0, 10);
  return { start, end };
}
