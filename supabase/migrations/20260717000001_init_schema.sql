-- Proto initial schema.
-- Translated from the JSON Schema domain aggregates in /schema.
--
-- Notes:
--  * RLS is ENABLED on every table with NO policies (default-deny). In Supabase,
--    a public-schema table with RLS off is reachable via the publishable/anon
--    key through PostgREST. Deny-all locks everything to the service role until
--    tenant policies are added in a follow-up migration (blocked on the
--    auth->DB bridge decision).
--  * gen_random_uuid() is built into Postgres 13+; no extension required.
--  * Sync keys (stytch_organization_id, stytch_member_id) are additions beyond
--    the JSON Schemas, needed to map Stytch Organizations/Members -> our rows.

begin;

-- ---------------------------------------------------------------------------
-- Enums
-- ---------------------------------------------------------------------------
create type tenant_status         as enum ('active', 'suspended', 'cancelled');
create type subscription_tier_code as enum ('free', 'pro', 'enterprise');
create type subscription_status   as enum ('trialing', 'active', 'past_due', 'cancelled');
create type user_status           as enum ('invited', 'active', 'deactivated');
create type tenant_role           as enum ('admin', 'member');
create type prototype_type        as enum ('functional', 'illustrative');
create type prototype_status      as enum ('draft', 'in_review', 'changes_requested', 'approved', 'archived');
create type participant_role      as enum ('reviewer', 'stakeholder');
create type ux_page_kind          as enum ('illustrative', 'functional');
create type hotspot_shape         as enum ('rect', 'polygon');
create type comment_status        as enum ('open', 'resolved');
create type comment_author_role   as enum ('owner', 'reviewer', 'stakeholder', 'member');
create type approval_status       as enum ('pending', 'approved', 'rejected', 'cancelled');
create type approval_decision     as enum ('pending', 'approved', 'rejected');

-- ---------------------------------------------------------------------------
-- updated_at trigger helper
-- ---------------------------------------------------------------------------
create or replace function set_updated_at() returns trigger as $$
begin
  new.updated_at = now();
  return new;
end;
$$ language plpgsql;

-- ---------------------------------------------------------------------------
-- subscription_tiers (platform catalog / reference data)
-- ---------------------------------------------------------------------------
create table subscription_tiers (
  id                 uuid primary key default gen_random_uuid(),
  code               subscription_tier_code not null unique,
  name               text not null,
  max_seats          integer not null check (max_seats >= 1),
  max_prototypes     integer check (max_prototypes >= 1), -- null = unlimited
  features           text[] not null default '{}',
  monthly_price_cents integer not null default 0 check (monthly_price_cents >= 0),
  currency           text not null default 'USD' check (currency ~ '^[A-Z]{3}$'),
  is_active          boolean not null default true
);

-- ---------------------------------------------------------------------------
-- tenants
-- ---------------------------------------------------------------------------
create table tenants (
  id                      uuid primary key default gen_random_uuid(),
  name                    text not null check (length(name) >= 1),
  slug                    text not null unique check (slug ~ '^[a-z0-9-]+$'),
  status                  tenant_status not null default 'active',
  subscription_tier_id    uuid not null references subscription_tiers(id),
  subscription_tier_code  subscription_tier_code not null,
  subscription_status     subscription_status not null default 'trialing',
  current_period_start    timestamptz,
  current_period_end      timestamptz,
  stytch_organization_id  text unique, -- sync key: Stytch Organization
  created_at              timestamptz not null default now(),
  updated_at              timestamptz not null default now()
);

-- ---------------------------------------------------------------------------
-- users (a Member within a Tenant/Organization)
-- ---------------------------------------------------------------------------
create table users (
  id                uuid primary key default gen_random_uuid(),
  tenant_id         uuid not null references tenants(id) on delete cascade,
  email             text not null check (position('@' in email) > 1),
  display_name      text not null check (length(display_name) >= 1),
  status            user_status not null default 'invited',
  tenant_role       tenant_role not null default 'member',
  stytch_member_id  text unique, -- sync key: Stytch Member
  last_login_at     timestamptz,
  created_at        timestamptz not null default now(),
  updated_at        timestamptz not null default now()
);

-- Case-insensitive unique email per tenant. Expression uniqueness requires an
-- index (not a table-level UNIQUE constraint).
create unique index users_tenant_lower_email_key on users (tenant_id, lower(email));

-- ---------------------------------------------------------------------------
-- user_identities (OAuth identities via Stytch)
-- ---------------------------------------------------------------------------
create table user_identities (
  id               uuid primary key default gen_random_uuid(),
  user_id          uuid not null references users(id) on delete cascade,
  tenant_id        uuid not null references tenants(id) on delete cascade,
  provider         text not null,
  provider_subject text,
  stytch_user_id   text not null,
  created_at       timestamptz not null default now(),
  unique (user_id, provider)
);

-- ---------------------------------------------------------------------------
-- prototypes
-- ---------------------------------------------------------------------------
create table prototypes (
  id                   uuid primary key default gen_random_uuid(),
  tenant_id            uuid not null references tenants(id) on delete cascade,
  name                 text not null check (length(name) >= 1),
  description          text,
  type                 prototype_type not null,
  status               prototype_status not null default 'draft',
  owner_id             uuid not null references users(id),
  github_repo_url      text,
  github_branch        text,
  last_deployed_commit text,
  created_at           timestamptz not null default now(),
  updated_at           timestamptz not null default now(),
  -- Functional prototypes must carry a source repo (mirrors the JSON Schema
  -- conditional required-when-functional rule).
  constraint functional_requires_repo
    check (type <> 'functional' or github_repo_url is not null)
);

-- ---------------------------------------------------------------------------
-- prototype_participants (reviewer / stakeholder roster; owner is on prototypes)
-- ---------------------------------------------------------------------------
create table prototype_participants (
  id           uuid primary key default gen_random_uuid(),
  prototype_id uuid not null references prototypes(id) on delete cascade,
  tenant_id    uuid not null references tenants(id) on delete cascade,
  user_id      uuid not null references users(id) on delete cascade,
  role         participant_role not null,
  added_at     timestamptz not null default now(),
  unique (prototype_id, user_id, role)
);

-- ---------------------------------------------------------------------------
-- ux_pages
-- ---------------------------------------------------------------------------
create table ux_pages (
  id            uuid primary key default gen_random_uuid(),
  prototype_id  uuid not null references prototypes(id) on delete cascade,
  tenant_id     uuid not null references tenants(id) on delete cascade,
  name          text not null check (length(name) >= 1),
  order_index   integer not null default 0 check (order_index >= 0),
  is_entry_page boolean not null default false,
  kind          ux_page_kind not null,
  image_media_id text,
  image_url     text,
  image_width   integer check (image_width >= 1),
  image_height  integer check (image_height >= 1),
  route         text,
  created_at    timestamptz not null default now(),
  updated_at    timestamptz not null default now(),
  -- Image fields are all-or-nothing.
  constraint image_all_or_none check (
    (image_media_id is null and image_url is null and image_width is null and image_height is null)
    or
    (image_media_id is not null and image_url is not null and image_width is not null and image_height is not null)
  )
);

-- ---------------------------------------------------------------------------
-- hotspots (clickable areas on illustrative pages)
-- ---------------------------------------------------------------------------
create table hotspots (
  id                 uuid primary key default gen_random_uuid(),
  ux_page_id         uuid not null references ux_pages(id) on delete cascade,
  tenant_id          uuid not null references tenants(id) on delete cascade,
  shape              hotspot_shape not null,
  rect_x             double precision check (rect_x >= 0),
  rect_y             double precision check (rect_y >= 0),
  rect_width         double precision check (rect_width >= 0),
  rect_height        double precision check (rect_height >= 0),
  points             jsonb,
  target_page_id     uuid references ux_pages(id) on delete set null,
  target_external_url text,
  created_at         timestamptz not null default now(),
  -- Enforce shape/body pairing at the DB layer (closes TD-1, which the JSON
  -- Schema intentionally left unenforced).
  constraint shape_body_matches check (
    (shape = 'rect'
      and rect_x is not null and rect_y is not null
      and rect_width is not null and rect_height is not null
      and points is null)
    or
    (shape = 'polygon' and points is not null
      and rect_x is null and rect_y is null
      and rect_width is null and rect_height is null)
  )
);

-- ---------------------------------------------------------------------------
-- comments
-- ---------------------------------------------------------------------------
create table comments (
  id                uuid primary key default gen_random_uuid(),
  prototype_id      uuid not null references prototypes(id) on delete cascade,
  tenant_id         uuid not null references tenants(id) on delete cascade,
  page_id           uuid references ux_pages(id) on delete set null,
  parent_comment_id uuid references comments(id) on delete cascade,
  author_id         uuid not null references users(id),
  author_role       comment_author_role,
  body              text not null check (length(body) >= 1),
  status            comment_status not null default 'open',
  created_at        timestamptz not null default now(),
  updated_at        timestamptz not null default now()
);

-- ---------------------------------------------------------------------------
-- approval_requests
-- ---------------------------------------------------------------------------
create table approval_requests (
  id              uuid primary key default gen_random_uuid(),
  prototype_id    uuid not null references prototypes(id) on delete cascade,
  tenant_id       uuid not null references tenants(id) on delete cascade,
  requested_by_id uuid not null references users(id),
  status          approval_status not null default 'pending',
  created_at      timestamptz not null default now(),
  resolved_at     timestamptz
);

-- ---------------------------------------------------------------------------
-- approval_decisions (one per stakeholder whose sign-off is required)
-- ---------------------------------------------------------------------------
create table approval_decisions (
  id                  uuid primary key default gen_random_uuid(),
  approval_request_id uuid not null references approval_requests(id) on delete cascade,
  tenant_id           uuid not null references tenants(id) on delete cascade,
  approver_id         uuid not null references users(id),
  decision            approval_decision not null default 'pending',
  comment             text,
  decided_at          timestamptz,
  unique (approval_request_id, approver_id)
);

-- ---------------------------------------------------------------------------
-- Indexes on foreign keys / common filters
-- ---------------------------------------------------------------------------
create index on tenants (subscription_tier_id);
create index on users (tenant_id);
create index on user_identities (user_id);
create index on user_identities (tenant_id);
create index on prototypes (tenant_id);
create index on prototypes (owner_id);
create index on prototype_participants (prototype_id);
create index on prototype_participants (tenant_id);
create index on prototype_participants (user_id);
create index on ux_pages (prototype_id);
create index on ux_pages (tenant_id);
create index on hotspots (ux_page_id);
create index on hotspots (tenant_id);
create index on hotspots (target_page_id);
create index on comments (prototype_id);
create index on comments (tenant_id);
create index on comments (page_id);
create index on comments (parent_comment_id);
create index on approval_requests (prototype_id);
create index on approval_requests (tenant_id);
create index on approval_decisions (approval_request_id);
create index on approval_decisions (tenant_id);

-- ---------------------------------------------------------------------------
-- updated_at triggers
-- ---------------------------------------------------------------------------
create trigger trg_tenants_updated_at    before update on tenants    for each row execute function set_updated_at();
create trigger trg_users_updated_at      before update on users      for each row execute function set_updated_at();
create trigger trg_prototypes_updated_at before update on prototypes for each row execute function set_updated_at();
create trigger trg_ux_pages_updated_at   before update on ux_pages   for each row execute function set_updated_at();
create trigger trg_comments_updated_at   before update on comments   for each row execute function set_updated_at();

-- ---------------------------------------------------------------------------
-- Row Level Security: enable everywhere, default-deny (no policies yet).
-- Locks all tables to the service role until tenant policies are added.
-- ---------------------------------------------------------------------------
alter table subscription_tiers     enable row level security;
alter table tenants                enable row level security;
alter table users                  enable row level security;
alter table user_identities        enable row level security;
alter table prototypes             enable row level security;
alter table prototype_participants enable row level security;
alter table ux_pages               enable row level security;
alter table hotspots               enable row level security;
alter table comments               enable row level security;
alter table approval_requests      enable row level security;
alter table approval_decisions     enable row level security;

commit;
