import type { Member, Organization } from '@stytch/react/b2b';
import type { TenantRole } from '../types/user';

// Adapts the Stytch session objects into a view model for the account page.
//
// This is deliberately NOT a domain `User` (src/types/user.ts): we are reading
// from Stytch, not our `users` table, so we do not have our own row id or
// tenant id (Stytch's member_id / organization_id live in a different
// namespace and map to users.stytch_member_id / tenants.stytch_organization_id,
// not to users.id / tenants.id). When the auth->DB bridge and RLS policies
// land, the account page should read the real `User` and this adapter can go.

export interface ProfileIdentity {
  /** OAuth provider, e.g. "Google". Comes from Stytch's provider_type. */
  provider: string;
  /** The provider's subject ("sub") for this member. */
  providerSubject: string;
  /** Provider profile picture, if the provider returned one. */
  profilePictureUrl: string | null;
}

export interface MemberProfile {
  displayName: string;
  email: string;
  emailVerified: boolean;
  /** Derived from Stytch's is_admin (the stytch_admin role). Maps to our
   *  users.tenant_role. */
  tenantRole: TenantRole;
  /** Raw Stytch member status string (e.g. "active", "invited", "pending").
   *  NOT our users.status enum — surfaced as-is and labelled accordingly. */
  stytchStatus: string;
  /** Stytch member_id = our users.stytch_member_id sync key. */
  stytchMemberId: string;
  mfaEnrolled: boolean;
  /** ISO-8601. */
  createdAt: string;
  /** ISO-8601. */
  updatedAt: string;
  identities: ProfileIdentity[];
  organization: {
    /** Stytch organization_name = our tenants.name. */
    name: string;
    /** Stytch organization_id = our tenants.stytch_organization_id sync key. */
    id: string;
    /** Stytch organization_slug = our tenants.slug. */
    slug: string;
  } | null;
}

/**
 * Build the account-page view model from the Stytch session objects.
 * Returns null when the member has not resolved yet (SDK still hydrating).
 */
export function toMemberProfile(
  member: Member | null | undefined,
  organization: Organization | null | undefined,
): MemberProfile | null {
  if (!member) return null;

  return {
    displayName: member.name || '',
    email: member.email_address,
    emailVerified: member.email_address_verified,
    tenantRole: member.is_admin ? 'admin' : 'member',
    stytchStatus: member.status,
    stytchMemberId: member.member_id,
    mfaEnrolled: member.mfa_enrolled,
    createdAt: member.created_at,
    updatedAt: member.updated_at,
    identities: (member.oauth_registrations ?? []).map((reg) => ({
      provider: reg.provider_type,
      providerSubject: reg.provider_subject,
      profilePictureUrl: reg.profile_picture_url,
    })),
    organization: organization
      ? {
          name: organization.organization_name,
          id: organization.organization_id,
          slug: organization.organization_slug,
        }
      : null,
  };
}
