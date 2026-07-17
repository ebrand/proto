import { describe, it, expect } from 'vitest';
import { toMemberProfile } from './memberProfile';

// Minimal fake Stytch Member with only the fields the mapper reads. Cast via
// `unknown` to Parameters<>[0] so we don't have to satisfy the full Stytch type
// surface just to exercise the mapping logic.
type MemberArg = Parameters<typeof toMemberProfile>[0];
type OrgArg = Parameters<typeof toMemberProfile>[1];

const baseMember = {
  name: 'Ada Lovelace',
  email_address: 'ada@example.com',
  email_address_verified: true,
  is_admin: true,
  status: 'active',
  member_id: 'member-test-123',
  mfa_enrolled: false,
  created_at: '2026-01-02T03:04:05Z',
  updated_at: '2026-02-03T04:05:06Z',
  oauth_registrations: [
    {
      provider_type: 'Google',
      provider_subject: 'google-sub-999',
      profile_picture_url: 'https://pic.example/ada.png',
    },
  ],
} as unknown as MemberArg;

const org = {
  organization_name: 'Analytical Engines',
  organization_id: 'org-test-777',
  organization_slug: 'analytical-engines',
} as unknown as OrgArg;

describe('toMemberProfile', () => {
  it('returns null while the member has not resolved', () => {
    expect(toMemberProfile(null, org)).toBeNull();
    expect(toMemberProfile(undefined, org)).toBeNull();
  });

  it('maps a full member + organization', () => {
    const p = toMemberProfile(baseMember, org)!;
    expect(p.displayName).toBe('Ada Lovelace');
    expect(p.email).toBe('ada@example.com');
    expect(p.emailVerified).toBe(true);
    expect(p.tenantRole).toBe('admin');
    expect(p.stytchStatus).toBe('active');
    expect(p.stytchMemberId).toBe('member-test-123');
    expect(p.mfaEnrolled).toBe(false);
    expect(p.createdAt).toBe('2026-01-02T03:04:05Z');
    expect(p.updatedAt).toBe('2026-02-03T04:05:06Z');
    expect(p.identities).toEqual([
      {
        provider: 'Google',
        providerSubject: 'google-sub-999',
        profilePictureUrl: 'https://pic.example/ada.png',
      },
    ]);
    expect(p.organization).toEqual({
      name: 'Analytical Engines',
      id: 'org-test-777',
      slug: 'analytical-engines',
    });
  });

  it('derives tenantRole=member when is_admin is false', () => {
    const p = toMemberProfile({ ...baseMember, is_admin: false } as MemberArg, org)!;
    expect(p.tenantRole).toBe('member');
  });

  it('tolerates a missing oauth_registrations array', () => {
    const noOauth = { ...baseMember };
    delete (noOauth as Record<string, unknown>).oauth_registrations;
    const p = toMemberProfile(noOauth as MemberArg, org)!;
    expect(p.identities).toEqual([]);
  });

  it('maps organization to null when there is none on the session', () => {
    const p = toMemberProfile(baseMember, null)!;
    expect(p.organization).toBeNull();
  });

  it('keeps an empty display name empty (page renders the dash)', () => {
    const p = toMemberProfile({ ...baseMember, name: '' } as MemberArg, org)!;
    expect(p.displayName).toBe('');
  });
});
