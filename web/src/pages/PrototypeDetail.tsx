import { type FormEvent, useCallback, useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { useStytchB2BClient } from '@stytch/react/b2b';
import {
  createPage,
  getPrototype,
  listPages,
  type PrototypeDetail as Prototype,
  type UxPage,
} from '../lib/api';

type LoadState = 'loading' | 'ready' | 'error';

export function PrototypeDetail() {
  const { id = '' } = useParams();
  const stytch = useStytchB2BClient();
  const [proto, setProto] = useState<Prototype | null>(null);
  const [pages, setPages] = useState<UxPage[]>([]);
  const [state, setState] = useState<LoadState>('loading');
  const [error, setError] = useState('');

  const [name, setName] = useState('');
  const [kind, setKind] = useState('illustrative');
  const [route, setRoute] = useState('');
  const [isEntry, setIsEntry] = useState(false);
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
      const [p, pg] = await Promise.all([getPrototype(token, id), listPages(token, id)]);
      setProto(p);
      setPages(pg);
      setState('ready');
    } catch (e: unknown) {
      setState('error');
      setError(e instanceof Error ? e.message : String(e));
    }
  }, [bearer, id]);

  useEffect(() => {
    void load();
  }, [load]);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (!name.trim()) {
      setFormError('Name is required.');
      return;
    }
    setBusy(true);
    setFormError('');
    try {
      await createPage(bearer(), id, {
        name: name.trim(),
        kind,
        isEntryPage: isEntry,
        route: kind === 'functional' && route.trim() ? route.trim() : undefined,
        orderIndex: pages.length,
      });
      setName('');
      setRoute('');
      setIsEntry(false);
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
        <h1>{proto?.name ?? 'Prototype'}</h1>
        <nav className="nav">
          <Link to="/prototypes">Prototypes</Link>
          <Link to="/dashboard">Dashboard</Link>
        </nav>
      </header>

      {state === 'loading' && <p className="muted">Loading…</p>}
      {state === 'error' && <p className="error">{error}</p>}

      {state === 'ready' && proto && (
        <>
          <section className="card">
            <h2>Details</h2>
            <dl className="kv">
              <dt>Type</dt>
              <dd>{proto.type}</dd>
              <dt>Status</dt>
              <dd>{proto.status}</dd>
              {proto.description && (
                <>
                  <dt>Description</dt>
                  <dd>{proto.description}</dd>
                </>
              )}
              {proto.githubRepoUrl && (
                <>
                  <dt>Repo</dt>
                  <dd className="mono">{proto.githubRepoUrl}</dd>
                </>
              )}
            </dl>
          </section>

          <section className="card">
            <h2>Add a page</h2>
            <form onSubmit={submit}>
              <div className="field">
                <label htmlFor="pgName">Name</label>
                <input
                  id="pgName"
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                  placeholder="Home"
                  disabled={busy}
                />
              </div>
              <div className="field">
                <label htmlFor="pgKind">Kind</label>
                <select id="pgKind" value={kind} onChange={(e) => setKind(e.target.value)} disabled={busy}>
                  <option value="illustrative">Illustrative (mockup)</option>
                  <option value="functional">Functional (route)</option>
                </select>
              </div>
              {kind === 'functional' && (
                <div className="field">
                  <label htmlFor="pgRoute">Route</label>
                  <input
                    id="pgRoute"
                    value={route}
                    onChange={(e) => setRoute(e.target.value)}
                    placeholder="/settings"
                    disabled={busy}
                  />
                </div>
              )}
              <label className="checkbox">
                <input type="checkbox" checked={isEntry} onChange={(e) => setIsEntry(e.target.checked)} disabled={busy} />
                Entry page
              </label>
              {formError && <p className="error">{formError}</p>}
              <button type="submit" disabled={busy}>
                {busy ? 'Adding…' : 'Add page'}
              </button>
            </form>
          </section>

          <section className="card">
            <h2>Pages</h2>
            {pages.length === 0 && <p className="muted">No pages yet — add one above.</p>}
            {pages.length > 0 && (
              <ul className="proto-list">
                {pages.map((pg) => (
                  <li key={pg.id}>
                    <strong>{pg.name}</strong>
                    <span className="muted">
                      {' '}
                      · {pg.kind}
                      {pg.isEntryPage ? ' · entry' : ''}
                      {pg.route ? ` · ${pg.route}` : ''}
                    </span>
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
