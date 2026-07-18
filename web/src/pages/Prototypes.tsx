import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { useStytchB2BClient } from '@stytch/react/b2b';
import {
  getPrototype,
  listHotspots,
  listPages,
  listPrototypes,
  type Hotspot,
  type PrototypeSummary,
  type UxPage,
} from '../lib/api';
import { NewPrototypeWizard } from '../components/NewPrototypeWizard';
import { PrototypePlayer } from '../components/PrototypePlayer';

type ListState = 'loading' | 'ready' | 'error';

interface PlayerData {
  name: string;
  pages: UxPage[];
  hotspots: Hotspot[];
  liveUrl?: string;
}

export function Prototypes() {
  const stytch = useStytchB2BClient();
  const [items, setItems] = useState<PrototypeSummary[]>([]);
  const [state, setState] = useState<ListState>('loading');
  const [error, setError] = useState('');

  const [player, setPlayer] = useState<PlayerData | null>(null);
  const [previewBusyId, setPreviewBusyId] = useState<string | null>(null);
  const [previewError, setPreviewError] = useState('');

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

  const openPreview = async (p: PrototypeSummary) => {
    const token = bearer();
    if (!token) return;
    setPreviewBusyId(p.id);
    setPreviewError('');
    try {
      if (p.type === 'functional') {
        const detail = await getPrototype(token, p.id);
        if (!detail.runUrl) {
          setPreviewError(`"${p.name}" isn't running yet — open it and use Build & run first.`);
          return;
        }
        setPlayer({ name: p.name, pages: [], hotspots: [], liveUrl: detail.runUrl });
        return;
      }
      const [pages, hotspots] = await Promise.all([listPages(token, p.id), listHotspots(token, p.id)]);
      setPlayer({ name: p.name, pages, hotspots });
    } catch (e: unknown) {
      setPreviewError(e instanceof Error ? e.message : String(e));
    } finally {
      setPreviewBusyId(null);
    }
  };

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
        {previewError && <p className="error">{previewError}</p>}
        {state === 'ready' && items.length > 0 && (
          <ul className="proto-list">
            {items.map((p) => (
              <li key={p.id} className="proto-row">
                <div>
                  <div>
                    <Link to={`/prototypes/${p.id}`}>
                      <strong>{p.name}</strong>
                    </Link>
                    <span className="muted"> · {p.type} · {p.status}</span>
                  </div>
                  {p.description && <div className="muted">{p.description}</div>}
                </div>
                <button
                  type="button"
                  className="play-btn"
                  onClick={() => void openPreview(p)}
                  disabled={previewBusyId === p.id}
                >
                  {previewBusyId === p.id ? 'Loading…' : '▶ Preview'}
                </button>
              </li>
            ))}
          </ul>
        )}
      </section>

      {player && (
        <PrototypePlayer
          prototypeName={player.name}
          pages={player.pages}
          hotspots={player.hotspots}
          liveUrl={player.liveUrl}
          onClose={() => setPlayer(null)}
        />
      )}
    </div>
  );
}
