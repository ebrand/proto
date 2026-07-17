import {
  useStytchB2BClient,
  useStytchMember,
  useStytchOrganization,
} from '@stytch/react/b2b';

// Placeholder authenticated landing page. Confirms the session resolved to a
// Member within an Organization (our User within a Tenant) and offers logout.
export function Dashboard() {
  const stytch = useStytchB2BClient();
  const { member } = useStytchMember();
  const { organization } = useStytchOrganization();

  const logout = () => {
    void stytch.session.revoke();
  };

  return (
    <div className="page">
      <header className="page-header">
        <h1>Proto</h1>
        <button onClick={logout}>Sign out</button>
      </header>

      <section className="card">
        <h2>Session</h2>
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
