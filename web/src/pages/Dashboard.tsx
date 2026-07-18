import { Link } from 'react-router-dom';
import {
  useStytchB2BClient,
  useStytchMember,
  useStytchOrganization,
} from '@stytch/react/b2b';
import { useMe } from '../hooks/useMe';
import { InviteForm } from '../components/InviteForm';

// Authenticated landing page. Shows the DB-backed Proto tenant/user (from
// /api/me) plus the raw Stytch session, and offers logout.
export function Dashboard() {
  const stytch = useStytchB2BClient();
  const { member } = useStytchMember();
  const { organization } = useStytchOrganization();
  const { me, state, error } = useMe();

  const logout = () => {
    void stytch.session.revoke();
  };

  return (
    <div className="page">
      <header className="page-header">
        <h1>Proto</h1>
        <nav className="nav">
          <Link to="/prototypes">Prototypes</Link>
          <Link to="/account">Account</Link>
          <button onClick={logout}>Sign out</button>
        </nav>
      </header>

      <section className="card">
        <h2>Tenant (Proto)</h2>
        {state === 'loading' && <p className="muted">Loading your tenant…</p>}
        {state === 'error' && <p className="error">Couldn’t load /api/me: {error}</p>}
        {state === 'not_provisioned' && (
          <p className="muted">This session isn’t linked to a Proto tenant yet.</p>
        )}
        {state === 'ready' && me && (
          <dl className="kv">
            <dt>Tenant</dt>
            <dd>{me.tenant.name}</dd>
            <dt>Slug</dt>
            <dd className="mono">{me.tenant.slug}</dd>
            <dt>Plan</dt>
            <dd>
              {me.tenant.subscriptionTierCode}{' '}
              <span className="muted">({me.tenant.subscriptionStatus})</span>
            </dd>
            <dt>Your role</dt>
            <dd>{me.user.tenantRole}</dd>
            <dt>You</dt>
            <dd>
              {me.user.displayName} <span className="muted">&lt;{me.user.email}&gt;</span>
            </dd>
          </dl>
        )}
      </section>

      {state === 'ready' && me?.user.tenantRole === 'admin' && (
        <section className="card">
          <h2>Invite a teammate</h2>
          <InviteForm />
        </section>
      )}

      <section className="card">
        <h2>Session (Stytch)</h2>
        <dl className="kv">
          <dt>Member</dt>
          <dd>{member?.email_address ?? '—'}</dd>
          <dt>Member ID</dt>
          <dd className="mono">{member?.member_id ?? '—'}</dd>
          <dt>Organization (Tenant)</dt>
          <dd>{organization?.organization_name ?? '—'}</dd>
          <dt>Organization ID</dt>
          <dd className="mono">{organization?.organization_id ?? '—'}</dd>
        </dl>
      </section>
    </div>
  );
}
