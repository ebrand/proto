import { type FormEvent, useEffect, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useStytchB2BClient, useStytchMemberSession } from '@stytch/react/b2b';
import { signupTenant } from '../lib/api';

// Minimal shape we read from a discovered organization.
type DiscoveredOrg = {
  organization: { organization_id: string; organization_name: string };
};

const TIERS = [
  { code: 'free', label: 'Free — up to 3 seats' },
  { code: 'pro', label: 'Pro — up to 25 seats' },
  { code: 'enterprise', label: 'Enterprise' },
];

type Phase = 'loading' | 'choose' | 'create' | 'error';

// Callback route for the Stytch discovery OAuth flow. Unlike the prebuilt
// StytchB2B component, this does NOT auto-create an organization: it exchanges
// the OAuth token for an intermediate session token (IST), then lets the user
// either join a discovered org or name + provision a NEW tenant via our .NET
// API (deferred creation — see the Tenant Provisioning design).
export function Authenticate() {
  const stytch = useStytchB2BClient();
  const navigate = useNavigate();
  const { session, isInitialized } = useStytchMemberSession();

  const [phase, setPhase] = useState<Phase>('loading');
  const [ist, setIst] = useState('');
  const [orgs, setOrgs] = useState<DiscoveredOrg[]>([]);
  const [orgName, setOrgName] = useState('');
  const [tier, setTier] = useState('free');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const started = useRef(false);

  // Already signed in (e.g. returning here with a live session) → leave.
  useEffect(() => {
    if (isInitialized && session) navigate('/dashboard', { replace: true });
  }, [isInitialized, session, navigate]);

  // Exchange the discovery OAuth token for an IST exactly once.
  useEffect(() => {
    if (started.current) return;
    started.current = true;

    const params = new URLSearchParams(window.location.search);
    const token = params.get('token');
    if (!token || params.get('stytch_token_type') !== 'discovery_oauth') {
      // No token in the URL — nothing to complete here.
      setPhase('error');
      setError('Missing or invalid discovery token. Start again from sign-in.');
      return;
    }

    stytch.oauth.discovery
      .authenticate({ discovery_oauth_token: token })
      .then((res) => {
        setIst(res.intermediate_session_token);
        setOrgs(res.discovered_organizations);
        setPhase(res.discovered_organizations.length > 0 ? 'choose' : 'create');
      })
      .catch((e: unknown) => {
        setPhase('error');
        setError(e instanceof Error ? e.message : String(e));
      });
  }, [stytch]);

  const joinOrg = async (organizationId: string) => {
    setBusy(true);
    setError('');
    try {
      await stytch.discovery.intermediateSessions.exchange({
        organization_id: organizationId,
        session_duration_minutes: 60,
      });
      navigate('/dashboard', { replace: true });
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : String(e));
      setBusy(false);
    }
  };

  const createTenant = async (e: FormEvent) => {
    e.preventDefault();
    if (!orgName.trim()) {
      setError('Enter an organization name.');
      return;
    }
    setBusy(true);
    setError('');
    try {
      const result = await signupTenant({
        intermediateSessionToken: ist,
        organizationName: orgName.trim(),
        tierCode: tier,
      });
      // Adopt the backend-minted session in the frontend SDK, then validate it.
      stytch.session.updateSession({
        session_token: result.sessionToken,
        session_jwt: result.sessionJwt,
      });
      await stytch.session.authenticate({ session_duration_minutes: 60 });
      navigate('/dashboard', { replace: true });
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : String(e));
      setBusy(false);
    }
  };

  return (
    <div className="auth-shell">
      <div className="auth-card">
        <h1>Proto</h1>

        {phase === 'loading' && <p className="muted">Completing sign-in…</p>}

        {phase === 'error' && (
          <>
            <p className="error">{error}</p>
            <button onClick={() => navigate('/login', { replace: true })}>Back to sign-in</button>
          </>
        )}

        {phase === 'choose' && (
          <>
            <p className="muted">Select an organization to continue</p>
            <ul className="org-list">
              {orgs.map((o) => (
                <li key={o.organization.organization_id}>
                  <button disabled={busy} onClick={() => joinOrg(o.organization.organization_id)}>
                    {o.organization.organization_name}
                  </button>
                </li>
              ))}
            </ul>
            <button className="link" disabled={busy} onClick={() => setPhase('create')}>
              Create a new organization instead
            </button>
            {error && <p className="error">{error}</p>}
          </>
        )}

        {phase === 'create' && (
          <form onSubmit={createTenant}>
            <p className="muted">Name your organization and pick a plan</p>
            <div className="field">
              <label htmlFor="orgName">Organization name</label>
              <input
                id="orgName"
                value={orgName}
                onChange={(e) => setOrgName(e.target.value)}
                placeholder="Acme Inc"
                autoFocus
                disabled={busy}
              />
            </div>
            <div className="field">
              <label htmlFor="tier">Plan</label>
              <select id="tier" value={tier} onChange={(e) => setTier(e.target.value)} disabled={busy}>
                {TIERS.map((t) => (
                  <option key={t.code} value={t.code}>
                    {t.label}
                  </option>
                ))}
              </select>
            </div>
            {error && <p className="error">{error}</p>}
            <button type="submit" disabled={busy}>
              {busy ? 'Creating…' : 'Create organization'}
            </button>
          </form>
        )}
      </div>
    </div>
  );
}
