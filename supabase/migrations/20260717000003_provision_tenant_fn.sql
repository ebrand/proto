-- provision_tenant(): atomic tenant + admin-user creation for the signup flow.
--
-- Called by the .NET API (Proto.Api) via PostgREST RPC with the service role,
-- immediately after it creates the Stytch organization for a new signup. Doing
-- both inserts in one function = one transaction, so a failure can't leave a
-- tenant row without its admin user. Tier is validated here (not trusted from
-- the client). SECURITY DEFINER + empty search_path per Supabase guidance;
-- every object is schema-qualified.
create or replace function public.provision_tenant(
  p_tier_code         subscription_tier_code,
  p_org_name          text,
  p_org_slug          text,
  p_stytch_org_id     text,
  p_member_email      text,
  p_member_name       text,
  p_stytch_member_id  text
) returns table (tenant_id uuid, user_id uuid)
language plpgsql
security definer
set search_path = ''
as $$
declare
  v_tier_id   uuid;
  v_tenant_id uuid;
  v_user_id   uuid;
begin
  select id into v_tier_id
  from public.subscription_tiers
  where code = p_tier_code and is_active;

  if v_tier_id is null then
    raise exception 'unknown or inactive subscription tier: %', p_tier_code
      using errcode = 'no_data_found';
  end if;

  insert into public.tenants
    (name, slug, subscription_tier_id, subscription_tier_code, stytch_organization_id)
  values
    (p_org_name, p_org_slug, v_tier_id, p_tier_code, p_stytch_org_id)
  returning id into v_tenant_id;

  insert into public.users
    (tenant_id, email, display_name, status, tenant_role, stytch_member_id)
  values
    (v_tenant_id, p_member_email, p_member_name, 'active', 'admin', p_stytch_member_id)
  returning id into v_user_id;

  return query select v_tenant_id, v_user_id;
end;
$$;

-- Only the service role (used by the .NET API) may execute this; never the
-- anon/publishable role from a browser.
revoke all on function public.provision_tenant(
  subscription_tier_code, text, text, text, text, text, text) from public, anon, authenticated;
grant execute on function public.provision_tenant(
  subscription_tier_code, text, text, text, text, text, text) to service_role;
