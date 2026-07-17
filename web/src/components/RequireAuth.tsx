import type { ReactNode } from 'react';
import { Navigate } from 'react-router-dom';
import { useStytchMemberSession } from '@stytch/react/b2b';

// Gate for authenticated routes. While the SDK is still hydrating the session
// from storage we must not redirect, or a logged-in member gets bounced to
// /login on every refresh before the session resolves.
export function RequireAuth({ children }: { children: ReactNode }) {
  const { session, isInitialized } = useStytchMemberSession();

  if (!isInitialized) {
    return <div className="centered muted">Loading…</div>;
  }

  if (!session) {
    return <Navigate to="/login" replace />;
  }

  return <>{children}</>;
}
