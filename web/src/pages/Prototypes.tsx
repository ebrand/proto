import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { useStytchB2BClient } from '@stytch/react/b2b';
import { listPrototypes, type PrototypeSummary } from '../lib/api';
import { NewPrototypeWizard } from '../components/NewPrototypeWizard';

type ListState = 'loading' | 'ready' | 'error';

export function Prototypes() {
  const stytch = useStytchB2BClient();
  const [items, setItems] = useState<PrototypeSummary[]>([]);
  const [state, setState] = useState<ListState>('loading');
  const [error, setError] = useState('');

  const load = useCallback(async () => {
    const tokens = stytch.session.getTokens();
    const token = tokens?.session_jwt || tokens?.session_token || '';
    if (!token) {
      setState('error');
      setError('No active session.');
      return;
    }
    try {
      setItems(await listPrototypes(token));
      setState('ready');
    } catch (e: unknown) {
      setState('error');
      setError(e instanceof Error ? e.message : String(e));
    }
  }, [stytch]);

  useEffect(() => {
    void load();
  }, [load]);

  return (
    <div className="page">
      <header className="page-header">
        <h1>Prototypes</h1>
        <nav className="nav">
          <Link to="/dashboard">Dashboard</Link>
        </nav>
      </header>

      <section className="card">
        <h2>New prototype</h2>
        <NewPrototypeWizard onCreated={load} />
      </section>

      <section className="card">
        <h2>Your prototypes</h2>
        {state === 'loading' && <p className="muted">Loading…</p>}
        {state === 'error' && <p className="error">{error}</p>}
        {state === 'ready' && items.length === 0 && (
          <p className="muted">No prototypes yet — create one above.</p>
        )}
        {state === 'ready' && items.length > 0 && (
          <ul className="proto-list">
            {items.map((p) => (
              <li key={p.id}>
                <div>
                  <Link to={`/prototypes/${p.id}`}>
                    <strong>{p.name}</strong>
                  </Link>
                  <span className="muted"> · {p.type} · {p.status}</span>
                </div>
                {p.description && <div className="muted">{p.description}</div>}
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}
