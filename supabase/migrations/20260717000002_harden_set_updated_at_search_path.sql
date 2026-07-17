-- Harden the set_updated_at() trigger function.
--
-- The init migration created set_updated_at() without a fixed search_path,
-- which trips Supabase's function_search_path_mutable lint (0011). A mutable
-- search_path lets a caller's role-level search_path influence name resolution
-- inside the function. This function only touches NEW (no schema-qualified
-- lookups), so pinning search_path to empty is safe and closes the warning.
alter function public.set_updated_at() set search_path = '';
