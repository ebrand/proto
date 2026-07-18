import { type FormEvent, type PointerEvent, useCallback, useEffect, useRef, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { useStytchB2BClient } from '@stytch/react/b2b';
import {
  createPage,
  getPrototype,
  listPages,
  updatePagePosition,
  type PrototypeDetail as Prototype,
  type UxPage,
} from '../lib/api';

type LoadState = 'loading' | 'ready' | 'error';

interface Pos {
  x: number;
  y: number;
}

// Frame geometry — kept in sync with .frame in index.css.
const FRAME_W = 180;
const FRAME_H = 96;
const GAP = 36;
const MARGIN = 24;
const COLS = 4;

// Default grid slot for a page with no stored position, by list order.
function autoPos(index: number): Pos {
  const col = index % COLS;
  const row = Math.floor(index / COLS);
  return {
    x: MARGIN + col * (FRAME_W + GAP),
    y: MARGIN + row * (FRAME_H + GAP),
  };
}

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

  // Canvas positions, keyed by page id. Local state so dragging is smooth;
  // seeded from the server's canvasX/Y (or an auto-grid slot when unset).
  const [positions, setPositions] = useState<Record<string, Pos>>({});
  const [saveError, setSaveError] = useState('');
  const [draggingId, setDraggingId] = useState<string | null>(null);
  const canvasRef = useRef<HTMLDivElement | null>(null);
  const drag = useRef<{ id: string; offX: number; offY: number; x: number; y: number; moved: boolean } | null>(null);

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

  // Reconcile positions whenever the page set changes: a stored position always
  // wins; an unplaced page keeps any local position, else gets an auto slot;
  // positions for removed pages are dropped.
  useEffect(() => {
    setPositions((prev) => {
      const next: Record<string, Pos> = {};
      pages.forEach((pg, i) => {
        if (pg.canvasX != null && pg.canvasY != null) {
          next[pg.id] = { x: pg.canvasX, y: pg.canvasY };
        } else {
          next[pg.id] = prev[pg.id] ?? autoPos(i);
        }
      });
      return next;
    });
  }, [pages]);

  const pointFromEvent = (e: PointerEvent): Pos | null => {
    const canvas = canvasRef.current;
    if (!canvas) return null;
    const rect = canvas.getBoundingClientRect();
    return {
      x: e.clientX - rect.left + canvas.scrollLeft,
      y: e.clientY - rect.top + canvas.scrollTop,
    };
  };

  const onPointerDown = (e: PointerEvent, pageId: string) => {
    if (e.button !== 0) return;
    const pt = pointFromEvent(e);
    const cur = positions[pageId];
    if (!pt || !cur) return;
    drag.current = { id: pageId, offX: pt.x - cur.x, offY: pt.y - cur.y, x: cur.x, y: cur.y, moved: false };
    setDraggingId(pageId);
    (e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
    e.preventDefault();
  };

  const onPointerMove = (e: PointerEvent, pageId: string) => {
    const d = drag.current;
    if (!d || d.id !== pageId) return;
    const pt = pointFromEvent(e);
    if (!pt) return;
    const x = Math.max(0, Math.round(pt.x - d.offX));
    const y = Math.max(0, Math.round(pt.y - d.offY));
    d.x = x;
    d.y = y;
    d.moved = true;
    setPositions((p) => ({ ...p, [pageId]: { x, y } }));
  };

  const onPointerUp = (e: PointerEvent, pageId: string) => {
    const d = drag.current;
    drag.current = null;
    setDraggingId(null);
    if (!d || d.id !== pageId) return;
    (e.currentTarget as HTMLElement).releasePointerCapture(e.pointerId);
    if (!d.moved) return; // a click, not a drag — nothing to persist
    const token = bearer();
    if (!token) return;
    setSaveError('');
    updatePagePosition(token, id, pageId, d.x, d.y).catch((err: unknown) => {
      setSaveError(err instanceof Error ? err.message : String(err));
    });
  };

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

  // Size the scrollable surface to contain every frame, with room to drag out.
  const surfaceW = Math.max(
    720,
    ...Object.values(positions).map((p) => p.x + FRAME_W + MARGIN),
  );
  const surfaceH = Math.max(
    480,
    ...Object.values(positions).map((p) => p.y + FRAME_H + MARGIN),
  );

  return (
    <div className="page page--wide">
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
            <h2>Flow map</h2>
            <div className="canvas-hint">
              <span className="muted">Drag pages to arrange the flow.</span>
              {saveError && <span className="error">{saveError}</span>}
            </div>
            <div className="canvas" ref={canvasRef}>
              <div className="canvas-surface" style={{ width: surfaceW, height: surfaceH }}>
                {pages.length === 0 && (
                  <div className="canvas-empty">No pages yet — add one above.</div>
                )}
                {pages.map((pg) => {
                  const pos = positions[pg.id] ?? { x: MARGIN, y: MARGIN };
                  return (
                    <div
                      key={pg.id}
                      className={`frame${pg.isEntryPage ? ' entry' : ''}${draggingId === pg.id ? ' dragging' : ''}`}
                      style={{ left: pos.x, top: pos.y }}
                      onPointerDown={(e) => onPointerDown(e, pg.id)}
                      onPointerMove={(e) => onPointerMove(e, pg.id)}
                      onPointerUp={(e) => onPointerUp(e, pg.id)}
                    >
                      <span className="frame-name">{pg.name}</span>
                      <div className="frame-badges">
                        <span className="badge">{pg.kind}</span>
                        {pg.isEntryPage && <span className="badge entry">entry</span>}
                        {pg.route && <span className="badge mono">{pg.route}</span>}
                      </div>
                    </div>
                  );
                })}
              </div>
            </div>
          </section>
        </>
      )}
    </div>
  );
}
