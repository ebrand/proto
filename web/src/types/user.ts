// TypeScript mirror of schema/user.schema.json (proto/user.schema.json).
// Keep this in sync with that JSON Schema by hand — it is the source of truth.
// These types describe our domain `User` (a Member within a Tenant), which is
// NOT the same shape as a Stytch Member. See src/lib/memberProfile.ts for the
// mapping used by the account page while we read from Stytch rather than the DB.

/** users.status — lifecycle within a tenant. */
export type UserStatus = 'invited' | 'active' | 'deactivated';

/** users.tenant_role — tenant-level admin vs ordinary member. Distinct from
 *  per-prototype roles (reviewer/stakeholder). */
export type TenantRole = 'admin' | 'member';

/** One OAuth identity linked to a user (via Stytch). */
export interface UserIdentity {
  /** OAuth IdP, e.g. "google", "microsoft", "github". */
  provider: string;
  /** The provider's subject ("sub") for this user, if known. */
  providerSubject?: string;
  stytchUserId: string;
}

export interface User {
  id: string;
  tenantId: string;
  email: string;
  displayName: string;
  status: UserStatus;
  tenantRole: TenantRole;
  identities?: UserIdentity[];
  /** ISO-8601 timestamp, or null if the user has never logged in. */
  lastLoginAt?: string | null;
  /** ISO-8601 timestamp. */
  createdAt: string;
  /** ISO-8601 timestamp. */
  updatedAt?: string;
}
