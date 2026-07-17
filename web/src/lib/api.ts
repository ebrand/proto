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
