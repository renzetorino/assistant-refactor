// src/api/labels.js
import { supabase } from '../supabase';

export async function listLabels() {
  const { data, error } = await supabase
    .from('labels')
    .select('*')
    .order('name');
  if (error) throw error;
  return data ?? [];
}

export async function createLabel({ name, color }) {
  const { data: u } = await supabase.auth.getUser();
  if (!u?.user) throw new Error('Not authenticated');

  // basic defaults/sanitization
  const safeName = String(name || '').trim();
  const safeColor = String(color || '#999999').trim();

  if (!safeName) throw new Error('Label name is required');

  const { data, error } = await supabase
    .from('labels')
    .insert({
      user_id: u.user.id,
      name: safeName,
      color: safeColor,
    })
    .select('*')
    .single();

  if (error) throw error;
  return data;
}


export async function deleteLabel(id) {
  if (!id) throw new Error('Missing label id');
  const { data: u } = await supabase.auth.getUser();
  if (!u?.user) throw new Error('Not authenticated');

  // 1) Remove dependencies in the join table to satisfy FK constraints
  const dep = await supabase
    .from('expense_labels')
    .delete()
    .eq('label_id', id);
  if (dep.error) throw dep.error;

  // 2) Delete the label itself
  const del = await supabase
    .from('labels')
    .delete()
    .eq('id', id)
    .eq('user_id', u.user.id);
  if (del.error) throw del.error;

  return true;
}
