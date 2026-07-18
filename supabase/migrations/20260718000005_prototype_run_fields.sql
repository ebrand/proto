-- Functional prototype runtime: track the build + the running Cloud Run service.
-- build_status is null until the first build. run_url is the HTTPS URL Proto
-- embeds once the service is deployed.
alter table prototypes add column if not exists build_status text;   -- building | ready | failed
alter table prototypes add column if not exists build_id     text;   -- Cloud Build id (for polling)
alter table prototypes add column if not exists build_error  text;   -- failure detail, when failed
alter table prototypes add column if not exists run_url      text;   -- Cloud Run HTTPS URL
alter table prototypes add column if not exists run_service  text;   -- Cloud Run service name (for teardown)
alter table prototypes add column if not exists last_built_at timestamptz;
