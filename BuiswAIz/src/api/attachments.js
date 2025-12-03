import { supabase } from '../supabase';

export async function uploadAttachments(expenseId, files) {
  const { data: u } = await supabase.auth.getUser();
  if (!u?.user) throw new Error('Not authenticated');

  const out = [];
  for (const file of files) {
    if (!['image/jpeg','image/png'].includes(file.type)) throw new Error('Only JPEG/PNG allowed');
    const ext = file.name.split('.').pop() || (file.type === 'image/png' ? 'png' : 'jpg');
    const key = `user/${u.user.id}/expenses/${expenseId}/${crypto.randomUUID()}.${ext}`;

    const up = await supabase.storage.from('receipts').upload(key, file, {
      contentType: file.type, cacheControl: '3600', upsert: false
    });
    if (up.error) throw up.error;

    const ins = await supabase
      .from('attachments')
      .insert({ user_id: u.user.id, expense_id: expenseId, storage_key: key, mime_type: file.type, size_bytes: file.size })
      .select('*').single();
    if (ins.error) throw ins.error;
    out.push(ins.data);
  }
  return out;
}

export async function listAttachments(expenseId) {
  const { data, error } = await supabase
    .from('attachments')
    .select('*')
    .eq('expense_id', expenseId)
    .order('uploaded_at', { ascending: false });
  if (error) throw error;
  const rows = data ?? [];
  return Promise.all(rows.map(async (a) => {
    const { data: u } = await supabase.storage.from('receipts').createSignedUrl(a.storage_key, 600);
    return { ...a, url: u.signedUrl };
  }));
}

export async function deleteAttachment(id) {
  const { data, error } = await supabase.from('attachments').select('*').eq('id', id).single();
  if (error || !data) throw error ?? new Error('Attachment not found');
  await supabase.from('attachments').delete().eq('id', id);
  await supabase.storage.from('receipts').remove([data.storage_key]);
}
