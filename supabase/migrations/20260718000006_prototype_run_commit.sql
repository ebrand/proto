-- Skip-if-unchanged: remember which git commit the running service was built
-- from, so a Build & run with no repo change can reuse it instead of rebuilding.
alter table prototypes add column if not exists run_commit text;
