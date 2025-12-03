const { data: tables } = await supabase
  .from('information_schema.tables')
  .select('table_name')
  .eq('table_schema', 'public');