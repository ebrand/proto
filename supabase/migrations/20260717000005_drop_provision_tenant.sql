-- Drop provision_tenant(): tenant/user provisioning now lives in the .NET API
-- (Proto.Api, TenantRepository) as an Npgsql transaction, per the decision to
-- keep all data logic in C# rather than in PL/pgSQL. Migration 000003 created
-- it; this retires it. The subscription_tiers seed (000004) stays.
drop function if exists public.provision_tenant(
  subscription_tier_code, text, text, text, text, text, text);
