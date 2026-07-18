import { type PointerEvent, useRef, useState } from 'react';
import type { Hotspot, UxPage } from '../lib/api';

interface Rect {
  x: number;
  y: number;
  width: number;
  height: number;
}

interface Props {
  page: UxPage;
  pages: UxPage[];
  hotspots: Hotspot[]; // all prototype hotspots; filtered to this page here
  onCreate: (rect: Rect, targetPageId: string, label: string) => Promise<void>;
  onDelete: (hotspotId: string) => Promise<void>;
  onClose: () => void;
}

// Smallest drawable region (fraction of the image), so a stray click isn't a
// zero-area hotspot.
const MIN_SIZE = 0.02;

function normFromEvent(e: PointerEvent, el: HTMLElement): { x: number; y: number } {
  const r = el.getBoundingClientRect();
  return {
    x: Math.min(1, Math.max(0, (e.clientX - r.left) / r.width)),
    y: Math.min(1, Math.max(0, (e.clientY - r.top) / r.height)),
  };
}

function rectFrom(a: { x: number; y: number }, b: { x: number; y: number }): Rect {
  return {
    x: Math.min(a.x, b.x),
    y: Math.min(a.y, b.y),
    width: Math.abs(a.x - b.x),
    height: Math.abs(a.y - b.y),
  };
}

const pct = (n: number) => `${n * 100}%`;

export function HotspotRegionEditor({ page, pages, hotspots, onCreate, onDelete, onClose }: Props) {
  const surfaceRef = useRef<HTMLDivElement | null>(null);
  const start = useRef<{ x: number; y: number } | null>(null);

  const [live, setLive] = useState<Rect | null>(null); // rectangle being dragged
  const [pending, setPending] = useState<Rect | null>(null); // drawn, awaiting a target
  const [target, setTarget] = useState('');
  const [label, setLabel] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');

  const pageHotspots = hotspots.filter((h) => h.uxPageId === page.id && h.rectX != null);
  const others = pages.filter((p) => p.id !== page.id);

  const onPointerDown = (e: PointerEvent) => {
    if (e.button !== 0 || pending || !surfaceRef.current) return;
    start.current = normFromEvent(e, surfaceRef.current);
    setLive({ ...start.current, width: 0, height: 0 });
    surfaceRef.current.setPointerCapture(e.pointerId);
    e.preventDefault();
  };

  const onPointerMove = (e: PointerEvent) => {
    if (!start.current || !surfaceRef.current) return;
    setLive(rectFrom(start.current, normFromEvent(e, surfaceRef.current)));
  };

  const onPointerUp = (e: PointerEvent) => {
    if (!start.current || !surfaceRef.current) return;
    const rect = rectFrom(start.current, normFromEvent(e, surfaceRef.current));
    surfaceRef.current.releasePointerCapture(e.pointerId);
    start.current = null;
    setLive(null);
    if (rect.width >= MIN_SIZE && rect.height >= MIN_SIZE) {
      setPending(rect);
      setError('');
    }
  };

  const confirm = async () => {
    if (!pending) return;
    if (!target) {
      setError('Pick a target page for this region.');
      return;
    }
    setBusy(true);
    setError('');
    try {
      await onCreate(pending, target, label.trim());
      setPending(null);
      setLabel('');
      setTarget('');
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  };

  const remove = async (id: string) => {
    setBusy(true);
    setError('');
    try {
      await onDelete(id);
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  };

  const targetName = (h: Hotspot) =>
    h.targetPageId ? (pages.find((p) => p.id === h.targetPageId)?.name ?? '(page)') : h.targetExternalUrl;

  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div className="modal region-modal" onClick={(e) => e.stopPropagation()}>
        <header className="region-head">
          <h2>Regions · {page.name}</h2>
          <button type="button" className="link" onClick={onClose}>Close</button>
        </header>
        <p className="muted">Drag on the image to draw a clickable region, then choose where it goes.</p>
        {error && <p className="error">{error}</p>}

        <div
          className="region-surface"
          ref={surfaceRef}
          onPointerDown={onPointerDown}
          onPointerMove={onPointerMove}
          onPointerUp={onPointerUp}
        >
          {page.imageUrl && <img src={page.imageUrl} alt={page.name} draggable={false} />}

          {pageHotspots.map((h) => (
            <div
              key={h.id}
              className="region existing"
              style={{ left: pct(h.rectX!), top: pct(h.rectY!), width: pct(h.rectWidth!), height: pct(h.rectHeight!) }}
              title={targetName(h) ?? ''}
            >
              <span className="region-tag">
                {h.label ? `${h.label} → ` : '→ '}{targetName(h)}
                <button type="button" className="region-x" disabled={busy} onClick={() => void remove(h.id)}>×</button>
              </span>
            </div>
          ))}

          {live && (
            <div className="region drawing" style={{ left: pct(live.x), top: pct(live.y), width: pct(live.width), height: pct(live.height) }} />
          )}
          {pending && (
            <div className="region pending" style={{ left: pct(pending.x), top: pct(pending.y), width: pct(pending.width), height: pct(pending.height) }} />
          )}
        </div>

        {pending && (
          <div className="region-form">
            <div className="field">
              <label htmlFor="regionTarget">Target page</label>
              <select id="regionTarget" value={target} onChange={(e) => setTarget(e.target.value)} disabled={busy}>
                <option value="">Select…</option>
                {others.map((p) => (
                  <option key={p.id} value={p.id}>{p.name}</option>
                ))}
              </select>
            </div>
            <div className="field">
              <label htmlFor="regionLabel">CTA label (optional)</label>
              <input id="regionLabel" value={label} onChange={(e) => setLabel(e.target.value)} placeholder="e.g. Buy now" disabled={busy} />
            </div>
            <div className="region-actions">
              <button type="button" onClick={() => void confirm()} disabled={busy}>{busy ? 'Adding…' : 'Add region'}</button>
              <button type="button" className="link" onClick={() => setPending(null)} disabled={busy}>Discard</button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
