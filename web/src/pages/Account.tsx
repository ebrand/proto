import { Link } from 'react-router-dom';
import {
  useStytchB2BClient,
  useStytchMember,
  useStytchOrganization,
} from '@stytch/react/b2b';
import { toMemberProfile } from '../lib/memberProfile';

// Format an ISO-8601 timestamp for display; fall back to a dash on empty/bad
// input rather than rendering "Invalid Date".
function formatDate(iso: string): string {
  if (!iso) return '—';
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleString();
}

// Read-only account page. Sourced entirely from the Stytch session (member +
// organization) — we do NOT read our `users` table here, because it is still
// default-deny RLS with no browser-readable policies. Fields are labelled to
// make clear which are Stytch's view vs. our domain concepts.
export function Account() {
  const stytch = useStytchB2BClient();
  const { member } = useStytchMember();
  const { organization } = useStytchOrganization();

  const profile = toMemberProfile(member, organization);

  const logout = () => {
    void stytch.session.revoke();
  };

  return (
    <div className="page">
      <header className="page-header">
        <h1>Account</h1>
        <nav className="nav">
          <Link to="/dashboard">Dashboard</Link>
          <button onClick={logout}>Sign out</button>
        </nav>
      </header>

      {!profile ? (
        <section className="card">
          <p className="muted">Loading your profile…</p>
        </section>
      ) : (
        <>
          <section className="card">
            <h2>Profile</h2>
            <dl className="kv">
              <dt>Display name</dt>
              <dd>{profile.displayName || <span className="muted">—</span>}</dd>
              <dt>Email</dt>
              <dd>
                {profile.email}{' '}
                {profile.emailVerified ? (
                  <span className="muted">(verified)</span>
                ) : (
                  <span className="muted">(unverified)</span>
                )}
              </dd>
              <dt>Tenant role</dt>
              <dd>{profile.tenantRole}</dd>
              <dt>Status</dt>
              <dd>
                {profile.stytchStatus}{' '}
                <span className="muted">(Stytch)</span>
              </dd>
              <dt>MFA</dt>
              <dd>{profile.mfaEnrolled ? 'Enrolled' : 'Not enrolled'}</dd>
              <dt>Member ID</dt>
              <dd className="mono">{profile.stytchMemberId}</dd>
              <dt>Created</dt>
              <dd>{formatDate(profile.createdAt)}</dd>
              <dt>Updated</dt>
              <dd>{formatDate(profile.updatedAt)}</dd>
            </dl>
          </section>

          <section className="card">
            <h2>Organization</h2>
            {profile.organization ? (
              <dl className="kv">
                <dt>Name</dt>
                <dd>{profile.organization.name}</dd>
                <dt>Slug</dt>
                <dd className="mono">{profile.organization.slug}</dd>
                <dt>Organization ID</dt>
                <dd className="mono">{profile.organization.id}</dd>
              </dl>
            ) : (
              <p className="muted">No organization on the session.</p>
            )}
          </section>

          <section className="card">
            <h2>Linked identities</h2>
            {profile.identities.length === 0 ? (
              <p className="muted">No OAuth identities linked.</p>
            ) : (
              <ul className="identity-list">
                {profile.identities.map((id) => (
                  <li key={`${id.provider}:${id.providerSubject}`}>
                    <span>{id.provider}</span>
                    <span className="mono muted">{id.providerSubject}</span>
                  </li>
                ))}
              </ul>
            )}
          </section>
        </>
      )}
    </div>
  );
}
