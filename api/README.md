# Proto.Api — .NET Core REST API

Backend trust boundary for Proto. Sits between the React app (`../web`) and both
Stytch and Supabase. It is the only component that holds the Stytch **secret**
key and the Supabase **service role**; the browser never touches Supabase
directly. Design: Confluence → Proto → *Tenant Provisioning & Onboarding (Design)*.

> Status: **scaffold**. Wiring, config, CORS, and the endpoint surface exist and
> build/run. The onboarding/invite/me handlers are stubs returning `501`.

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
dotnet user-secrets set "Supabase:ConnectionString" "Host=…;Database=postgres;Username=…;Password=…"
```

In deployment, use environment variables (e.g. `Stytch__ProjectSecret`).

The Stytch values are the same project as `../web` (source of truth:
`../auth/stytch.json`). The Supabase connection string points at the project
referenced in `../.mcp.json`.

## Endpoints (current)

| Method | Path | Status |
| --- | --- | --- |
| GET | `/api/health` | implemented |
| POST | `/api/onboarding/signup` | stub (501) |
| POST | `/api/invitations` | stub (501) |
| GET | `/api/invitations/callback` | stub (501) |
| GET | `/api/me` | stub (501) |

## Packages

- `Stytch.net` — official Stytch B2B backend SDK (depends on Newtonsoft.Json).
- `Npgsql` — Postgres access to Supabase (service-role connection).
