# Proto.Api — .NET Core REST API

Backend trust boundary for Proto. Sits between the React app (`../web`) and both
Stytch and Supabase. It is the only component that holds the Stytch **secret**
key and the Supabase **service role**; the browser never touches Supabase
directly. Design: Confluence → Proto → *Tenant Provisioning & Onboarding (Design)*.

> Status: **early**. `POST /api/onboarding/signup` (Flow 2) is implemented —
> creates the Stytch org from an intermediate session token, then writes the
> tenant + admin user atomically via Npgsql. The invite/me handlers are still
> stubs returning `501`. All data logic is in C# (no PL/pgSQL functions).

## Requirements

- .NET SDK 10.x (`dotnet --version`)

## Run

```bash
dotnet run
```

Then probe the health endpoint (reports whether creds are present, without
leaking them):

```bash
curl -k https://localhost:<port>/api/health
# {"status":"ok","stytchConfigured":false,"supabaseConfigured":false}
```

The port is printed on startup; OpenAPI is served at `/openapi/v1.json` in
Development.

## Configuration & secrets

Config sections live in `appsettings.json` with **empty** placeholders. Never
commit real secrets. Supply them via user-secrets in development:

```bash
dotnet user-secrets init
dotnet user-secrets set "Stytch:ProjectId"     "project-test-…"
dotnet user-secrets set "Stytch:ProjectSecret" "secret-test-…"
dotnet user-secrets set "Supabase:ConnectionString" "Host=aws-0-<region>.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.<ref>;Password=…;SSL Mode=Require;Trust Server Certificate=true"
```

In deployment, use environment variables (e.g. `Stytch__ProjectSecret`).

> **Use the Supabase _Session pooler_ connection string**, not the direct one.
> The direct host `db.<ref>.supabase.co` is IPv6-only and won't resolve on
> IPv4 networks; the pooler host `aws-0-<region>.pooler.supabase.com` (user
> `postgres.<ref>`) is IPv4-compatible.

The Stytch values are the same project as `../web` (source of truth:
`../auth/stytch.json`). The Supabase connection string is the `session-pooler-cnn-string`
in `../auth/supabase.json`.

## Endpoints (current)

| Method | Path | Status |
| --- | --- | --- |
| GET | `/api/health` | implemented |
| POST | `/api/onboarding/signup` | implemented (Flow 2) |
| POST | `/api/invitations` | stub (501) |
| GET | `/api/invitations/callback` | stub (501) |
| GET | `/api/me` | stub (501) |

Signup depends on the `subscription_tiers` catalog being seeded (migration
`20260717000004_seed_subscription_tiers`).

## Packages

- `Stytch.net` — official Stytch B2B backend SDK (depends on Newtonsoft.Json).
- `Npgsql` — Postgres access to Supabase via the session pooler.
