import { useEffect, useMemo, useState } from 'react';
import type { Hotspot, UxPage } from '../lib/api';

interface Props {
  prototypeName: string;
  pages: UxPage[];
  hotspots: Hotspot[];
  onClose: () => void;
  liveUrl?: string; // when set, render the running functional app instead of pages
}

const pct = (n: number) => `${n * 100}%`;

// Read-only player. For functional prototypes with a running URL it embeds the
// live app in an iframe; otherwise it plays illustrative pages — start at the
// entry page and click hotspot regions to navigate.
export function PrototypePlayer({ prototypeName, pages, hotspots, onClose, liveUrl }: Props) {
  const entry = useMemo(
    () => pages.find((p) => p.isEntryPage) ?? [...pages].sort((a, b) => a.orderIndex - b.orderIndex)[0] ?? null,
    [pages],
  );

  const [currentId, setCurrentId] = useState<string | null>(entry?.id ?? null);
  const [history, setHistory] = useState<string[]>([]);
  const [showAreas, setShowAreas] = useState(false);

  const current = pages.find((p) => p.id === currentId) ?? null;
  // Larger areas first so smaller, more-specific regions render on top (later in
  // the DOM) and stay clickable — e.g. a small button drawn over a whole-page link.
  const pageHotspots = hotspots
    .filter((h) => h.uxPageId === currentId && h.rectX != null)
    .sort((a, b) => (b.rectWidth! * b.rectHeight!) - (a.rectWidth! * a.rectHeight!));

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);

  const navigate = (h: Hotspot) => {
    if (h.targetPageId) {
      if (currentId) setHistory((hst) => [...hst, currentId]);
      setCurrentId(h.targetPageId);
    } else if (h.targetExternalUrl) {
      window.open(h.targetExternalUrl, '_blank', 'noopener,noreferrer');
    }
  };

  const back = () => {
    if (!history.length) return;
    setCurrentId(history[history.length - 1]);
    setHistory(history.slice(0, -1));
  };

  const restart = () => {
    setCurrentId(entry?.id ?? null);
    setHistory([]);
  };

  // Functional prototype: embed the running app in an iframe.
  if (liveUrl) {
    return (
      <div className="player-backdrop">
        <div className="player-bar">
          <span className="player-title">
            <strong>{prototypeName}</strong>
            <span className="muted"> · live</span>
          </span>
          <div className="player-controls">
            <a href={liveUrl} target="_blank" rel="noopener noreferrer"><button type="button">Open in tab ↗</button></a>
            <button type="button" onClick={onClose}>Exit</button>
          </div>
        </div>
        <div className="player-stage player-stage--live">
          <iframe className="player-live" src={liveUrl} title={prototypeName} />
        </div>
      </div>
    );
  }

  return (
    <div className="player-backdrop">
      <div className="player-bar">
        <span className="player-title">
          <strong>{prototypeName}</strong>
          <span className="muted"> · {current?.name ?? '—'}</span>
        </span>
        <div className="player-controls">
          <button type="button" onClick={() => setShowAreas((s) => !s)}>
            {showAreas ? 'Hide areas' : 'Show areas'}
          </button>
          <button type="button" onClick={back} disabled={!history.length}>Back</button>
          <button type="button" onClick={restart}>Restart</button>
          <button type="button" onClick={onClose}>Exit</button>
        </div>
      </div>

      <div className="player-stage">
        {current?.imageUrl ? (
          <div className="player-frame">
            <img src={current.imageUrl} alt={current.name} draggable={false} />
            {pageHotspots.map((h) => (
              <button
                key={h.id}
                type="button"
                className={`player-hotspot${showAreas ? ' show' : ''}`}
                style={{ left: pct(h.rectX!), top: pct(h.rectY!), width: pct(h.rectWidth!), height: pct(h.rectHeight!) }}
                title={h.label ?? undefined}
                onClick={() => navigate(h)}
              />
            ))}
          </div>
        ) : (
          <div className="player-empty">
            {current ? (
              <>
                <p><strong>{current.name}</strong> has no image to display.</p>
                {current.kind === 'functional' && <p className="muted">Functional pages aren’t runnable yet.</p>}
              </>
            ) : (
              <p>No page to show. Add pages and set an entry page.</p>
            )}
          </div>
        )}
      </div>
    </div>
  );
}
