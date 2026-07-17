import { useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { StytchB2B, useStytchMemberSession } from '@stytch/react/b2b';
import { authConfig } from '../config/authConfig';

// Callback route. Stytch redirects here after Google. The same StytchB2B
// component finishes the discovery flow (organization selection / creation and
// the intermediate-session exchange). Once a full member session exists we leave
// for the dashboard.
export function Authenticate() {
  const navigate = useNavigate();
  const { session, isInitialized } = useStytchMemberSession();

  useEffect(() => {
    if (isInitialized && session) {
      navigate('/dashboard', { replace: true });
    }
  }, [isInitialized, session, navigate]);

  return (
    <div className="auth-shell">
      <div className="auth-card">
        <h1>Proto</h1>
        <p className="muted">Completing sign-in…</p>
        <StytchB2B config={authConfig} />
      </div>
    </div>
  );
}
