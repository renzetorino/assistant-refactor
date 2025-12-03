// src/api/categories.js
import { supabase } from '../supabase';

/** Get main categories (parent_id IS NULL), globally shared */
export async function fetchMainCategories() {
  const { data, error } = await supabase
    .from('categories')
    .select('id, name, sort_order')
    .is('parent_id', null)
    .eq('is_active', true)
    .order('sort_order', { ascending: true })
    .order('name', { ascending: true });
  if (error) {
    console.error('fetchMainCategories error:', error);
    throw error;
  }
  return data ?? [];
}

/** Get subcategories (parent_id = given id), globally shared */
export async function fetchSubcategories(parentId) {
  if (!parentId) return [];
  const { data, error } = await supabase
    .from('categories')
    .select('id, name, sort_order, parent_id')
    .eq('parent_id', parentId)
    .eq('is_active', true)
    .order('sort_order', { ascending: true })
    .order('name', { ascending: true });
  if (error) {
    console.error('fetchSubcategories error:', error);
    throw error;
  }
  return data ?? [];
}


export async function getCategoryById(id) {
  if (!id) return null;
  const { data, error } = await supabase
    .from('categories')
    .select('id, name, parent_id')
    .eq('id', id)
    .maybeSingle();
  if (error) throw error;
  return data;
}