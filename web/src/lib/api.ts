// Client for the Proto .NET API (Proto.Api). Base URL is configurable; defaults
// to the API's dev `http` launch profile.
const API_BASE = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5070';

export interface SignupResult {
  sessionToken: string;
  sessionJwt: string;
  tenantId: string;
  organizationId: string;
}

export interface SignupInput {
  intermediateSessionToken: string;
  organizationName: string;
  tierCode: string;
}

/**
 * Flow 2: create the tenant (and its Stytch org) from the discovery
 * intermediate session token. Returns Stytch session tokens for the caller to
 * adopt via session.updateSession(...) + session.authenticate().
 */
export async function signupTenant(input: SignupInput): Promise<SignupResult> {
  const res = await fetch(`${API_BASE}/api/onboarding/signup`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(input),
  });

  if (!res.ok) {
    let detail = res.statusText;
    try {
      const body = (await res.json()) as { detail?: string; error?: string };
      detail = body.detail ?? body.error ?? detail;
    } catch {
      // non-JSON error body; keep statusText
    }
    throw new Error(`Signup failed (${res.status}): ${detail}`);
  }

  return (await res.json()) as SignupResult;
}

export interface Me {
  user: {
    id: string;
    email: string;
    displayName: string;
    status: string;
    tenantRole: string;
  };
  tenant: {
    id: string;
    name: string;
    slug: string;
    subscriptionTierCode: string;
    subscriptionStatus: string;
  };
}

/**
 * GET /api/me — resolve the current Stytch session to the Proto user + tenant.
 * Returns null when the session is valid but not yet provisioned (404).
 * `sessionToken` is the Stytch session token or JWT, sent as a Bearer.
 */
export async function getMe(sessionToken: string): Promise<Me | null> {
  const res = await fetch(`${API_BASE}/api/me`, {
    headers: { Authorization: `Bearer ${sessionToken}` },
  });

  if (res.status === 404) return null; // not_provisioned
  if (!res.ok) {
    let detail = res.statusText;
    try {
      const body = (await res.json()) as { detail?: string; error?: string };
      detail = body.detail ?? body.error ?? detail;
    } catch {
      // keep statusText
    }
    throw new Error(`/api/me failed (${res.status}): ${detail}`);
  }

  return (await res.json()) as Me;
}

async function bearerFetch(path: string, sessionToken: string, init?: RequestInit): Promise<Response> {
  return fetch(`${API_BASE}${path}`, {
    ...init,
    headers: { ...init?.headers, Authorization: `Bearer ${sessionToken}` },
  });
}

async function asError(res: Response, prefix: string): Promise<Error> {
  let detail = res.statusText;
  try {
    const body = (await res.json()) as { detail?: string; error?: string };
    detail = body.detail ?? body.error ?? detail;
  } catch {
    // keep statusText
  }
  return new Error(`${prefix} (${res.status}): ${detail}`);
}

/** Admin: invite an email into the current tenant. */
export async function sendInvite(sessionToken: string, email: string, tenantRole: string): Promise<void> {
  const res = await bearerFetch('/api/invitations', sessionToken, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, tenantRole }),
  });
  if (!res.ok) throw await asError(res, 'Invite failed');
}

/** Invitee: activate the pending users row after authenticating the magic link. */
export async function acceptInvite(sessionToken: string): Promise<Me> {
  const res = await bearerFetch('/api/invitations/accept', sessionToken, { method: 'POST' });
  if (!res.ok) throw await asError(res, 'Accept failed');
  return (await res.json()) as Me;
}

export interface PrototypeSummary {
  id: string;
  name: string;
  description: string | null;
  type: string;
  status: string;
  createdAt: string;
}

export interface CreatePrototypeInput {
  name: string;
  type: string;
  description?: string;
  githubRepoUrl?: string;
  githubBranch?: string;
  language?: string;
}

export interface RepoInspection {
  accessible: boolean;
  owner: string | null;
  repoName: string | null;
  defaultBranch: string | null;
  detectedLanguage: string | null;
  supported: boolean;
  isPrivate: boolean;
  message: string | null;
}

/** Inspect a GitHub repo: detect language + whether it's supported. */
export async function inspectRepo(sessionToken: string, repoUrl: string): Promise<RepoInspection> {
  const res = await bearerFetch('/api/prototypes/inspect', sessionToken, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ repoUrl }),
  });
  if (!res.ok) throw await asError(res, 'Inspecting repo failed');
  return (await res.json()) as RepoInspection;
}

/** List the caller's tenant's prototypes. */
export async function listPrototypes(sessionToken: string): Promise<PrototypeSummary[]> {
  const res = await bearerFetch('/api/prototypes', sessionToken);
  if (!res.ok) throw await asError(res, 'Loading prototypes failed');
  return (await res.json()) as PrototypeSummary[];
}

/** Create a prototype in the caller's tenant. */
export async function createPrototype(
  sessionToken: string,
  input: CreatePrototypeInput,
): Promise<{ id: string }> {
  const res = await bearerFetch('/api/prototypes', sessionToken, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(input),
  });
  if (!res.ok) throw await asError(res, 'Creating prototype failed');
  return (await res.json()) as { id: string };
}

export interface PrototypeDetail {
  id: string;
  name: string;
  description: string | null;
  type: string;
  status: string;
  ownerId: string;
  githubRepoUrl: string | null;
  githubBranch: string | null;
  createdAt: string;
  updatedAt: string;
}

/** Get one prototype (tenant-scoped). */
export async function getPrototype(sessionToken: string, id: string): Promise<PrototypeDetail> {
  const res = await bearerFetch(`/api/prototypes/${id}`, sessionToken);
  if (!res.ok) throw await asError(res, 'Loading prototype failed');
  return (await res.json()) as PrototypeDetail;
}

export interface UxPage {
  id: string;
  name: string;
  kind: string;
  orderIndex: number;
  isEntryPage: boolean;
  route: string | null;
  imageUrl: string | null;
  createdAt: string;
}

export interface CreatePageInput {
  name: string;
  kind: string;
  orderIndex?: number;
  isEntryPage?: boolean;
  route?: string;
}

/** List the pages of a prototype. */
export async function listPages(sessionToken: string, prototypeId: string): Promise<UxPage[]> {
  const res = await bearerFetch(`/api/prototypes/${prototypeId}/pages`, sessionToken);
  if (!res.ok) throw await asError(res, 'Loading pages failed');
  return (await res.json()) as UxPage[];
}

/** Add a page to a prototype. */
export async function createPage(
  sessionToken: string,
  prototypeId: string,
  input: CreatePageInput,
): Promise<{ id: string }> {
  const res = await bearerFetch(`/api/prototypes/${prototypeId}/pages`, sessionToken, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(input),
  });
  if (!res.ok) throw await asError(res, 'Creating page failed');
  return (await res.json()) as { id: string };
}
