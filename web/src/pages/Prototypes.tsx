import { type FormEvent, useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { useStytchB2BClient } from '@stytch/react/b2b';
import { createPrototype, listPrototypes, type PrototypeSummary } from '../lib/api';
// list items link to the prototype detail page

type ListState = 'loading' | 'ready' | 'error';

export function Prototypes() {
  const stytch = useStytchB2BClient();
  const [items, setItems] = useState<PrototypeSummary[]>([]);
  const [state, setState] = useState<ListState>('loading');
  const [error, setError] = useState('');

  const [name, setName] = useState('');
  const [type, setType] = useState('illustrative');
  const [description, setDescription] = useState('');
  const [repoUrl, setRepoUrl] = useState('');
  const [busy, setBusy] = useState(false);
  const [formError, setFormError] = useState('');

  const bearer = useCallback(() => {
    const tokens = stytch.session.getTokens();
    return tokens?.session_jwt || tokens?.session_token || '';
  }, [stytch]);

  const load = useCallback(async () => {
    const token = bearer();
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
  }, [bearer]);

  useEffect(() => {
    void load();
  }, [load]);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (!name.trim()) {
      setFormError('Name is required.');
      return;
    }
    if (type === 'functional' && !repoUrl.trim()) {
      setFormError('Functional prototypes need a GitHub repo URL.');
      return;
    }
    setBusy(true);
    setFormError('');
    try {
      await createPrototype(bearer(), {
        name: name.trim(),
        type,
        description: description.trim() || undefined,
        githubRepoUrl: repoUrl.trim() || undefined,
      });
      setName('');
      setDescription('');
      setRepoUrl('');
      await load();
    } catch (e: unknown) {
      setFormError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  };

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
        <form onSubmit={submit}>
          <div className="field">
            <label htmlFor="pName">Name</label>
            <input
              id="pName"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="Checkout redesign"
              disabled={busy}
            />
          </div>
          <div className="field">
            <label htmlFor="pType">Type</label>
            <select id="pType" value={type} onChange={(e) => setType(e.target.value)} disabled={busy}>
              <option value="illustrative">Illustrative (mockups)</option>
              <option value="functional">Functional (GitHub repo)</option>
            </select>
          </div>
          {type === 'functional' && (
            <div className="field">
              <label htmlFor="pRepo">GitHub repo URL</label>
              <input
                id="pRepo"
                value={repoUrl}
                onChange={(e) => setRepoUrl(e.target.value)}
                placeholder="https://github.com/org/repo"
                disabled={busy}
              />
            </div>
          )}
          <div className="field">
            <label htmlFor="pDesc">Description (optional)</label>
            <input
              id="pDesc"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              disabled={busy}
            />
          </div>
          {formError && <p className="error">{formError}</p>}
          <button type="submit" disabled={busy}>
            {busy ? 'Creating…' : 'Create prototype'}
          </button>
        </form>
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
