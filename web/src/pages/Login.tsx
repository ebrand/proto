import { StytchB2B } from '@stytch/react/b2b';
import { authConfig } from '../config/authConfig';

export function Login() {
  return (
    <div className="auth-shell">
      <div className="auth-card">
        <h1>Proto</h1>
        <p className="muted">Sign in to continue</p>
        <StytchB2B config={authConfig} />
      </div>
    </div>
  );
}
