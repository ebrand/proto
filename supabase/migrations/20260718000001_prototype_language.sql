-- Store the detected language for functional prototypes (from the repo, via the
-- "define a prototype" workflow's inspection step). Nullable — illustrative
-- prototypes have no language.
alter table prototypes add column if not exists language text;
