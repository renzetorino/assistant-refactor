import { supabase } from '../supabase';

export async function listContacts() {
  const { data, error } = await supabase
    .from('contacts')
    .select('id,name,email,phone')
    .order('name');
  if (error) throw error;
  return data ?? [];
}

export async function createContact(input) {
  const { data: u } = await supabase.auth.getUser();
  if (!u?.user) throw new Error('Not authenticated');
  const { data, error } = await supabase
    .from('contacts')
    .insert({
      user_id: u.user.id,        // ← set owner
      name: input.name,
      email: input.email ?? null,
      phone: input.phone ?? null,
      address: input.address ?? null,
      note: input.note ?? null,
    })
    .select('*')
    .single();
  if (error) throw error;
  return data;
}
