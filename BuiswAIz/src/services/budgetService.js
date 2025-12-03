// src/services/budgetService.js
import { supabase } from '../supabase';
import { listExpensesByMonth } from '../api/expenses'; // <-- single import only

/** Budgets list */
export async function listBudgets() {
  const { data, error } = await supabase
    .from('budget')
    .select('id, month_year, monthly_budget_amount')
    .order('month_year', { ascending: false });
  if (error) throw error;
  return data ?? [];
}

/** Read single budget by month ISO (YYYY-MM-01) */
export async function getBudgetByMonthISO(isoMonth) {
  const { data, error } = await supabase
    .from('budget')
    .select('id, month_year, monthly_budget_amount')
    .eq('month_year', isoMonth)
    .maybeSingle();
  if (error) throw error;
  return data || null;
}

/** Create a budget row (DB trigger will log INSERT) */
export async function createBudget(monthYYYYMM, amount) {
  const iso = `${monthYYYYMM}-01`;
  const { data, error } = await supabase
    .from('budget')
    .insert({ month_year: iso, monthly_budget_amount: Number(amount || 0) })
    .select('id, month_year, monthly_budget_amount')
    .single();
  if (error) throw error;
  return data;
}

/** Upsert a month (DB trigger logs INSERT or UPDATE) */
export async function upsertBudget(payload) {
  // payload.month_year should be an ISO date like "YYYY-MM-01"
  const { data, error } = await supabase
    .from("budgets") // <-- fix here
    .upsert(payload, { onConflict: "month_year" })
    .select("id, month_year, monthly_budget_amount");

  if (error) throw error;
  return data?.[0];
}

/** Edit existing budget by id (DB trigger logs UPDATE if amount changed) */
export async function updateBudgetById(budgetId, amount) {

  const { data, error } = await supabase
    .from('budget')
    .update({ monthly_budget_amount: Number(amount || 0) })
    .eq('id', budgetId)
    .select('id, month_year, monthly_budget_amount')
    .single();
  if (error) throw error;
  return data;
}

/** Budget change log */
export async function getBudgetHistory(budgetId) {
  if (!budgetId) return [];
  const { data, error } = await supabase
    .from('budgethistory')
    .select('id, old_amount, new_amount, created_at')
    .eq('budget_id', budgetId)
    .order('created_at', { ascending: false });
  if (error) throw error;
  return data ?? [];
}

/** Adapter used by BudgetHistory.jsx to fetch month expenses from ISO month */
export async function getExpensesForBudgetMonth(isoMonth) {
  if (!isoMonth) return [];
  const [yyyyStr, mmStr] = String(isoMonth).slice(0, 7).split('-');
  const yyyy = Number(yyyyStr);
  const mm = Number(mmStr);
  return await listExpensesByMonth(yyyy, mm);
}


