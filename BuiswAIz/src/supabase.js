import { createClient } from '@supabase/supabase-js';

const supabaseUrl = import.meta.env.VITE_SUPABASE_URL;
// const supabaseUrl = 'https://qyeegnjmzfyyhecbjomm.supabase.co';
const supabaseAnonKey = import.meta.env.VITE_SUPABASE_ANON_KEY;
;
// const supabaseAnonKey = 'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InF5ZWVnbmptemZ5eWhlY2Jqb21tIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NDkxOTcxNjEsImV4cCI6MjA2NDc3MzE2MX0.7s7bOszi1QX6X4mAFTOOenXYcFaus-7kAVhDmSAMirU';
export const supabase = createClient(supabaseUrl, supabaseAnonKey);
