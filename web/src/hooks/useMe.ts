import { useEffect, useState } from 'react';
import { useStytchB2BClient, useStytchMemberSession } from '@stytch/react/b2b';
import { getMe, type Me } from '../lib/api';

type MeState = 'loading' | 'ready' | 'not_provisioned' | 'error';

// Fetches the Proto user + tenant for the current session from GET /api/me,
// sending the Stytch session token as a Bearer. This is the DB-backed,
// authoritative identity (vs. the raw Stytch member/org the SDK exposes).
export function useMe(): { me: Me | null; state: MeState; error: string } {
  const stytch = useStytchB2BClient();
  const { session, isInitialized } = useStytchMemberSession();
  const [me, setMe] = useState<Me | null>(null);
  const [state, setState] = useState<MeState>('loading');
  const [error, setError] = useState('');

  useEffect(() => {
    if (!isInitialized) return;
    if (!session) {
      setState('error');
      setError('No active session.');
      return;
    }

    const tokens = stytch.session.getTokens();
    const bearer = tokens?.session_jwt || tokens?.session_token;
    if (!bearer) {
      setState('error');
      setError('Session token unavailable in the browser.');
      return;
    }

    let cancelled = false;
    setState('loading');
    getMe(bearer)
      .then((data) => {
        if (cancelled) return;
        if (data) {
          setMe(data);
          setState('ready');
        } else {
          setState('not_provisioned');
        }
      })
      .catch((e: unknown) => {
        if (cancelled) return;
        setState('error');
        setError(e instanceof Error ? e.message : String(e));
      });

    return () => {
      cancelled = true;
    };
  }, [isInitialized, session, stytch]);

  return { me, state, error };
}
