import { type FormEvent, type PointerEvent, useCallback, useEffect, useRef, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { useStytchB2BClient } from '@stytch/react/b2b';
import { HotspotRegionEditor } from '../components/HotspotRegionEditor';
import { PrototypePlayer } from '../components/PrototypePlayer';
import {
  buildPrototype,
  createHotspot,
  createPage,
  deleteHotspot,
  deletePage,
  deletePageImage,
  getBuildLog,
  getPrototype,
  listHotspots,
  listPages,
  renamePage,
  teardownPrototype,
  updatePagePosition,
  uploadPageImage,
  type Hotspot,
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

// Point on the border of a frame (centered at c, half-extents hw/hh) in the
// direction of `toward` — so an arrow meets the frame edge, not its center.
function edgePoint(c: Pos, hw: number, hh: number, toward: Pos): Pos {
  const dx = toward.x - c.x;
  const dy = toward.y - c.y;
  if (dx === 0 && dy === 0) return c;
  const t = 1 / Math.max(Math.abs(dx) / hw, Math.abs(dy) / hh);
  return { x: c.x + dx * t, y: c.y + dy * t };
}

export function PrototypeDetail() {
  const { id = '' } = useParams();
  const stytch = useStytchB2BClient();
  const [proto, setProto] = useState<Prototype | null>(null);
  const [pages, setPages] = useState<UxPage[]>([]);
  const [hotspots, setHotspots] = useState<Hotspot[]>([]);
  const [state, setState] = useState<LoadState>('loading');
  const [error, setError] = useState('');

  const [name, setName] = useState('');
  const [kind, setKind] = useState('illustrative');
  const [route, setRoute] = useState('');
  const [isEntry, setIsEntry] = useState(false);
  const [busy, setBusy] = useState(false);
  const [formError, setFormError] = useState('');

  // Add-a-link form.
  const [fromPage, setFromPage] = useState('');
  const [toPage, setToPage] = useState('');
  const [linkLabel, setLinkLabel] = useState('');
  const [linkBusy, setLinkBusy] = useState(false);
  const [linkError, setLinkError] = useState('');

  // Per-page image upload state.
  const [imgBusyPage, setImgBusyPage] = useState<string | null>(null);
  const [imgError, setImgError] = useState('');

  // Which page's hotspot regions are being edited (modal).
  const [editingPageId, setEditingPageId] = useState<string | null>(null);

  // Play/preview mode.
  const [playing, setPlaying] = useState(false);

  // Functional build+run.
  const [buildBusy, setBuildBusy] = useState(false);
  const [buildActionError, setBuildActionError] = useState('');
  const [showLog, setShowLog] = useState(false);
  const [buildLog, setBuildLog] = useState('');
  const [logBusy, setLogBusy] = useState(false);

  // Page rename/delete management.
  const [renamingId, setRenamingId] = useState<string | null>(null);
  const [renameValue, setRenameValue] = useState('');
  const [pageBusyId, setPageBusyId] = useState<string | null>(null);
  const [pageMgmtError, setPageMgmtError] = useState('');

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
      const [p, pg, hs] = await Promise.all([
        getPrototype(token, id),
        listPages(token, id),
        listHotspots(token, id),
      ]);
      setProto(p);
      setPages(pg);
      setHotspots(hs);
      setState('ready');
    } catch (e: unknown) {
      setState('error');
      setError(e instanceof Error ? e.message : String(e));
    }
  }, [bearer, id]);

  useEffect(() => {
    void load();
  }, [load]);

  // While a build is in flight, poll — each GET reconciles server-side and
  // flips to ready/failed, which stops the poll.
  useEffect(() => {
    if (proto?.buildStatus !== 'building') return;
    const t = setInterval(() => void load(), 4000);
    return () => clearInterval(t);
  }, [proto?.buildStatus, load]);

  const doBuild = async () => {
    setBuildBusy(true);
    setBuildActionError('');
    try {
      await buildPrototype(bearer(), id);
      await load();
    } catch (e: unknown) {
      setBuildActionError(e instanceof Error ? e.message : String(e));
    } finally {
      setBuildBusy(false);
    }
  };

  const loadLog = async () => {
    setLogBusy(true);
    try {
      setBuildLog(await getBuildLog(bearer(), id));
    } catch (e: unknown) {
      setBuildLog(e instanceof Error ? e.message : String(e));
    } finally {
      setLogBusy(false);
    }
  };

  const toggleLog = async () => {
    const next = !showLog;
    setShowLog(next);
    if (next) await loadLog();
  };

  const doTeardown = async () => {
    if (!window.confirm('Stop the running prototype? Its Cloud Run service will be deleted.')) return;
    setBuildBusy(true);
    setBuildActionError('');
    try {
      await teardownPrototype(bearer(), id);
      await load();
    } catch (e: unknown) {
      setBuildActionError(e instanceof Error ? e.message : String(e));
    } finally {
      setBuildBusy(false);
    }
  };

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

  const addLink = async (e: FormEvent) => {
    e.preventDefault();
    if (!fromPage || !toPage) {
      setLinkError('Pick both a source and a target page.');
      return;
    }
    if (fromPage === toPage) {
      setLinkError('A link must point at a different page.');
      return;
    }
    setLinkBusy(true);
    setLinkError('');
    try {
      await createHotspot(bearer(), id, fromPage, {
        targetPageId: toPage,
        label: linkLabel.trim() || undefined,
      });
      setLinkLabel('');
      await load();
    } catch (e: unknown) {
      setLinkError(e instanceof Error ? e.message : String(e));
    } finally {
      setLinkBusy(false);
    }
  };

  const removeLink = async (hs: Hotspot) => {
    const token = bearer();
    if (!token) return;
    setLinkError('');
    try {
      await deleteHotspot(token, id, hs.uxPageId, hs.id);
      await load();
    } catch (e: unknown) {
      setLinkError(e instanceof Error ? e.message : String(e));
    }
  };

  const onPickImage = async (pageId: string, file: File | undefined) => {
    if (!file) return;
    setImgBusyPage(pageId);
    setImgError('');
    try {
      await uploadPageImage(bearer(), id, pageId, file);
      await load();
    } catch (e: unknown) {
      setImgError(e instanceof Error ? e.message : String(e));
    } finally {
      setImgBusyPage(null);
    }
  };

  const onRemoveImage = async (pageId: string) => {
    setImgBusyPage(pageId);
    setImgError('');
    try {
      await deletePageImage(bearer(), id, pageId);
      await load();
    } catch (e: unknown) {
      setImgError(e instanceof Error ? e.message : String(e));
    } finally {
      setImgBusyPage(null);
    }
  };

  const startRename = (pg: UxPage) => {
    setRenamingId(pg.id);
    setRenameValue(pg.name);
    setPageMgmtError('');
  };

  const saveRename = async (pageId: string) => {
    const name = renameValue.trim();
    if (!name) {
      setPageMgmtError('Name is required.');
      return;
    }
    setPageBusyId(pageId);
    setPageMgmtError('');
    try {
      await renamePage(bearer(), id, pageId, name);
      setRenamingId(null);
      await load();
    } catch (e: unknown) {
      setPageMgmtError(e instanceof Error ? e.message : String(e));
    } finally {
      setPageBusyId(null);
    }
  };

  const doDeletePage = async (pg: UxPage) => {
    if (!window.confirm(`Delete "${pg.name}"? This also removes its links and image. This can't be undone.`)) {
      return;
    }
    setPageBusyId(pg.id);
    setPageMgmtError('');
    try {
      await deletePage(bearer(), id, pg.id);
      if (editingPageId === pg.id) setEditingPageId(null);
      await load();
    } catch (e: unknown) {
      setPageMgmtError(e instanceof Error ? e.message : String(e));
    } finally {
      setPageBusyId(null);
    }
  };

  const illustrativePages = pages.filter((p) => p.kind === 'illustrative');

  // Size the scrollable surface to contain every frame, with room to drag out.
  const surfaceW = Math.max(
    720,
    ...Object.values(positions).map((p) => p.x + FRAME_W + MARGIN),
  );
  const surfaceH = Math.max(
    480,
    ...Object.values(positions).map((p) => p.y + FRAME_H + MARGIN),
  );

  const pageName = (pid: string | null) => pages.find((p) => p.id === pid)?.name ?? '(unknown)';

  // Only page->page hotspots become arrows, and only when both endpoints exist
  // and differ.
  const validArrows = hotspots.filter(
    (h) => h.targetPageId && positions[h.uxPageId] && h.targetPageId in positions && h.uxPageId !== h.targetPageId,
  );

  // Group by unordered page pair so multiple links between the same two pages
  // (e.g. a bidirectional A<->B, or duplicates) can be fanned apart instead of
  // stacking on one line.
  const pairKey = (h: Hotspot) => [h.uxPageId, h.targetPageId as string].sort().join('|');
  const groups = new Map<string, Hotspot[]>();
  for (const h of validArrows) {
    const k = pairKey(h);
    (groups.get(k) ?? groups.set(k, []).get(k)!).push(h);
  }

  const ARROW_SPACING = 20;
  const arrows = validArrows.map((h) => {
    const src = positions[h.uxPageId];
    const tgt = positions[h.targetPageId as string];
    const srcC = { x: src.x + FRAME_W / 2, y: src.y + FRAME_H / 2 };
    const tgtC = { x: tgt.x + FRAME_W / 2, y: tgt.y + FRAME_H / 2 };
    let start = edgePoint(srcC, FRAME_W / 2, FRAME_H / 2, tgtC);
    let end = edgePoint(tgtC, FRAME_W / 2, FRAME_H / 2, srcC);

    const group = groups.get(pairKey(h))!;
    const n = group.length;
    if (n > 1) {
      // Perpendicular is derived from the pair's CANONICAL (sorted) direction,
      // not this arrow's own direction, so opposite-direction links in a pair
      // still land on distinct parallel lines rather than overlapping.
      const [pa, pb] = [h.uxPageId, h.targetPageId as string].sort();
      const ca = positions[pa];
      const cb = positions[pb];
      const cdx = cb.x - ca.x;
      const cdy = cb.y - ca.y;
      const clen = Math.hypot(cdx, cdy) || 1;
      const perp = { x: -cdy / clen, y: cdx / clen };
      const off = (group.indexOf(h) - (n - 1) / 2) * ARROW_SPACING;
      start = { x: start.x + perp.x * off, y: start.y + perp.y * off };
      end = { x: end.x + perp.x * off, y: end.y + perp.y * off };
      // Push each label further out along its own offset side so labels split.
      const extra = off >= 0 ? 10 : -10;
      const mid = { x: (start.x + end.x) / 2, y: (start.y + end.y) / 2 };
      return { h, start, end, labelPos: { x: mid.x + perp.x * extra, y: mid.y + perp.y * extra } };
    }
    // Lone link: label centered, lifted just above the line.
    const mid = { x: (start.x + end.x) / 2, y: (start.y + end.y) / 2 };
    return { h, start, end, labelPos: { x: mid.x, y: mid.y - 10 } };
  });

  return (
    <div className="page page--wide">
      <header className="page-header">
        <h1>{proto?.name ?? 'Prototype'}</h1>
        <nav className="nav">
          {state === 'ready' && proto && (proto.type === 'functional' ? !!proto.runUrl : pages.length > 0) && (
            <button type="button" className="play-btn" onClick={() => setPlaying(true)}>
              ▶ Preview
            </button>
          )}
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

          {proto.type === 'functional' && (
            <section className="card">
              <h2>Build &amp; run</h2>
              {!proto.githubRepoUrl ? (
                <p className="muted">No GitHub repo on this prototype.</p>
              ) : (
                <>
                  <div className="build-row">
                    {proto.buildStatus === 'building' && <span className="status-pill building">● Building…</span>}
                    {proto.buildStatus === 'ready' && <span className="status-pill ready">● Running</span>}
                    {proto.buildStatus === 'failed' && <span className="status-pill failed">● Build failed</span>}
                    {!proto.buildStatus && <span className="status-pill">Not built yet</span>}

                    {proto.buildStatus !== 'building' && (
                      <button type="button" onClick={() => void doBuild()} disabled={buildBusy}>
                        {buildBusy ? 'Starting…' : proto.buildStatus ? 'Rebuild' : 'Build & run'}
                      </button>
                    )}
                    {proto.buildStatus === 'ready' && proto.runUrl && (
                      <>
                        <button type="button" className="play-btn" onClick={() => setPlaying(true)}>▶ Preview</button>
                        <button type="button" className="link danger" onClick={() => void doTeardown()} disabled={buildBusy}>Stop</button>
                      </>
                    )}
                  </div>
                  {proto.buildStatus === 'building' && (
                    <p className="muted">Building the repo and deploying — this takes a couple of minutes.</p>
                  )}
                  {proto.buildStatus === 'ready' && proto.runUrl && (
                    <p className="build-url">
                      <a href={proto.runUrl} target="_blank" rel="noopener noreferrer">{proto.runUrl}</a>
                    </p>
                  )}
                  {proto.buildStatus === 'failed' && proto.buildError && <p className="error">{proto.buildError}</p>}
                  {buildActionError && <p className="error">{buildActionError}</p>}

                  {proto.buildStatus && (
                    <div className="build-log-controls">
                      <button type="button" className="link" onClick={() => void toggleLog()}>
                        {showLog ? 'Hide build log' : 'View build log'}
                      </button>
                      {showLog && (
                        <button type="button" className="link" onClick={() => void loadLog()} disabled={logBusy}>
                          {logBusy ? 'Refreshing…' : 'Refresh'}
                        </button>
                      )}
                    </div>
                  )}
                  {showLog && (
                    <pre className="build-log">{logBusy && !buildLog ? 'Loading…' : buildLog || 'No log yet.'}</pre>
                  )}
                </>
              )}
            </section>
          )}

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
            {pages.length === 0 ? (
              <p className="muted">No pages yet — add one above.</p>
            ) : (
              <>
                {pageMgmtError && <p className="error">{pageMgmtError}</p>}
                <ul className="proto-list page-list">
                  {pages.map((pg) => {
                    const busy = pageBusyId === pg.id;
                    return (
                      <li key={pg.id}>
                        {renamingId === pg.id ? (
                          <div className="page-rename">
                            <input
                              value={renameValue}
                              onChange={(e) => setRenameValue(e.target.value)}
                              disabled={busy}
                              autoFocus
                              onKeyDown={(e) => {
                                if (e.key === 'Enter') void saveRename(pg.id);
                                if (e.key === 'Escape') setRenamingId(null);
                              }}
                            />
                            <button type="button" onClick={() => void saveRename(pg.id)} disabled={busy}>
                              {busy ? 'Saving…' : 'Save'}
                            </button>
                            <button type="button" className="link" onClick={() => setRenamingId(null)} disabled={busy}>
                              Cancel
                            </button>
                          </div>
                        ) : (
                          <>
                            <span>
                              <strong>{pg.name}</strong>
                              <span className="muted">
                                {' '}· {pg.kind}
                                {pg.isEntryPage ? ' · entry' : ''}
                                {pg.route ? ` · ${pg.route}` : ''}
                              </span>
                            </span>
                            <span className="page-actions">
                              <button type="button" className="link" onClick={() => startRename(pg)} disabled={busy}>
                                Rename
                              </button>
                              <button type="button" className="link danger" onClick={() => void doDeletePage(pg)} disabled={busy}>
                                Delete
                              </button>
                            </span>
                          </>
                        )}
                      </li>
                    );
                  })}
                </ul>
              </>
            )}
          </section>

          <section className="card">
            <h2>Flow map</h2>
            <div className="canvas-hint">
              <span className="muted">Drag pages to arrange the flow. Arrows are links between pages.</span>
              {saveError && <span className="error">{saveError}</span>}
            </div>
            <div className="canvas" ref={canvasRef}>
              <div className="canvas-surface" style={{ width: surfaceW, height: surfaceH }}>
                {pages.length === 0 && (
                  <div className="canvas-empty">No pages yet — add one above.</div>
                )}
                <svg className="flow-arrows" width={surfaceW} height={surfaceH}>
                  <defs>
                    <marker
                      id="arrowhead"
                      markerWidth="9"
                      markerHeight="9"
                      refX="7"
                      refY="3"
                      orient="auto"
                      markerUnits="strokeWidth"
                    >
                      <path d="M0,0 L7,3 L0,6 Z" fill="currentColor" />
                    </marker>
                  </defs>
                  {arrows.map(({ h, start, end, labelPos }) => (
                    <g key={h.id}>
                      <line
                        x1={start.x}
                        y1={start.y}
                        x2={end.x}
                        y2={end.y}
                        stroke="currentColor"
                        strokeWidth={2}
                        markerEnd="url(#arrowhead)"
                      />
                      {h.label && (
                        <text x={labelPos.x} y={labelPos.y} className="arrow-label" textAnchor="middle">
                          {h.label}
                        </text>
                      )}
                    </g>
                  ))}
                </svg>
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
                      {pg.imageUrl && (
                        <img className="frame-thumb" src={pg.imageUrl} alt="" draggable={false} />
                      )}
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

          <section className="card">
            <h2>Page images</h2>
            {illustrativePages.length === 0 ? (
              <p className="muted">Illustrative pages can carry a mockup image. Add one above.</p>
            ) : (
              <>
                {imgError && <p className="error">{imgError}</p>}
                <ul className="proto-list image-list">
                  {illustrativePages.map((pg) => {
                    const uploading = imgBusyPage === pg.id;
                    return (
                      <li key={pg.id}>
                        <div className="image-row">
                          {pg.imageUrl ? (
                            <img className="image-thumb" src={pg.imageUrl} alt={pg.name} />
                          ) : (
                            <div className="image-thumb image-thumb--empty">no image</div>
                          )}
                          <strong>{pg.name}</strong>
                        </div>
                        <div className="image-actions">
                          <label className={`filebtn${uploading ? ' disabled' : ''}`}>
                            {uploading ? 'Uploading…' : pg.imageUrl ? 'Replace' : 'Upload'}
                            <input
                              type="file"
                              accept="image/png,image/jpeg,image/webp"
                              disabled={uploading}
                              onChange={(e) => {
                                void onPickImage(pg.id, e.target.files?.[0]);
                                e.target.value = '';
                              }}
                            />
                          </label>
                          {pg.imageUrl && (
                            <button type="button" className="filebtn" disabled={uploading} onClick={() => setEditingPageId(pg.id)}>
                              Edit regions
                            </button>
                          )}
                          {pg.imageUrl && (
                            <button type="button" className="link" disabled={uploading} onClick={() => void onRemoveImage(pg.id)}>
                              Remove
                            </button>
                          )}
                        </div>
                      </li>
                    );
                  })}
                </ul>
              </>
            )}
          </section>

          <section className="card">
            <h2>Links</h2>
            {pages.length < 2 ? (
              <p className="muted">Add at least two pages to link them.</p>
            ) : (
              <form className="link-form" onSubmit={addLink}>
                <div className="field">
                  <label htmlFor="fromPage">From page</label>
                  <select id="fromPage" value={fromPage} onChange={(e) => setFromPage(e.target.value)} disabled={linkBusy}>
                    <option value="">Select…</option>
                    {pages.map((p) => (
                      <option key={p.id} value={p.id}>{p.name}</option>
                    ))}
                  </select>
                </div>
                <div className="field">
                  <label htmlFor="toPage">To page</label>
                  <select id="toPage" value={toPage} onChange={(e) => setToPage(e.target.value)} disabled={linkBusy}>
                    <option value="">Select…</option>
                    {pages.filter((p) => p.id !== fromPage).map((p) => (
                      <option key={p.id} value={p.id}>{p.name}</option>
                    ))}
                  </select>
                </div>
                <div className="field">
                  <label htmlFor="linkLabel">CTA label (optional)</label>
                  <input
                    id="linkLabel"
                    value={linkLabel}
                    onChange={(e) => setLinkLabel(e.target.value)}
                    placeholder="e.g. Proceed to checkout"
                    disabled={linkBusy}
                  />
                </div>
                {linkError && <p className="error">{linkError}</p>}
                <button type="submit" disabled={linkBusy}>
                  {linkBusy ? 'Adding…' : 'Add link'}
                </button>
              </form>
            )}

            {hotspots.length > 0 && (
              <ul className="proto-list link-list">
                {hotspots.map((h) => (
                  <li key={h.id}>
                    <span>
                      <strong>{pageName(h.uxPageId)}</strong>
                      <span className="muted"> → </span>
                      <strong>{h.targetPageId ? pageName(h.targetPageId) : h.targetExternalUrl}</strong>
                      {h.label && <span className="muted"> · {h.label}</span>}
                    </span>
                    <button type="button" className="link" onClick={() => void removeLink(h)}>
                      Remove
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </section>

          {playing && (
            <PrototypePlayer
              prototypeName={proto.name}
              pages={pages}
              hotspots={hotspots}
              liveUrl={proto.type === 'functional' ? (proto.runUrl ?? undefined) : undefined}
              onClose={() => setPlaying(false)}
            />
          )}

          {editingPageId && (() => {
            const editPage = pages.find((p) => p.id === editingPageId);
            if (!editPage) return null;
            return (
              <HotspotRegionEditor
                page={editPage}
                pages={pages}
                hotspots={hotspots}
                onClose={() => setEditingPageId(null)}
                onCreate={async (rect, targetPageId, label) => {
                  await createHotspot(bearer(), id, editPage.id, {
                    targetPageId,
                    label: label || undefined,
                    rectX: rect.x,
                    rectY: rect.y,
                    rectWidth: rect.width,
                    rectHeight: rect.height,
                  });
                  await load();
                }}
                onDelete={async (hotspotId) => {
                  await deleteHotspot(bearer(), id, editPage.id, hotspotId);
                  await load();
                }}
              />
            );
          })()}
        </>
      )}
    </div>
  );
}
