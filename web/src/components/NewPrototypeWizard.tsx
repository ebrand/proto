import { type FormEvent, useState } from 'react';
import { useStytchB2BClient } from '@stytch/react/b2b';
import { createPrototype, inspectRepo, type RepoInspection } from '../lib/api';

// The constraints a functional prototype must satisfy (see the Prototype
// Contract). Shown so the developer acknowledges them before creating.
const CONSTRAINTS = [
  'Self-contained — no database, auth, cache, or external APIs (mock data only)',
  'No runtime secrets; serves HTTP on $PORT bound to 0.0.0.0',
  'Single process, one HTTP port, an HTML UI at /',
  'Ephemeral & stateless — survives restarts and scale-to-zero',
];

type Step = 'type' | 'repo' | 'details';
type Kind = 'illustrative' | 'functional';

export function NewPrototypeWizard({ onCreated }: { onCreated: () => void }) {
  const stytch = useStytchB2BClient();
  const bearer = () => {
    const t = stytch.session.getTokens();
    return t?.session_jwt || t?.session_token || '';
  };

  const [step, setStep] = useState<Step>('type');
  const [type, setType] = useState<Kind | ''>('');
  const [repoUrl, setRepoUrl] = useState('');
  const [inspecting, setInspecting] = useState(false);
  const [inspection, setInspection] = useState<RepoInspection | null>(null);
  const [ack, setAck] = useState(false);
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');

  const reset = () => {
    setStep('type');
    setType('');
    setRepoUrl('');
    setInspection(null);
    setAck(false);
    setName('');
    setDescription('');
    setError('');
  };

  const chooseType = (t: Kind) => {
    setType(t);
    setError('');
    setStep(t === 'functional' ? 'repo' : 'details');
  };

  const inspect = async () => {
    if (!repoUrl.trim()) return;
    setInspecting(true);
    setError('');
    setInspection(null);
    setAck(false);
    try {
      setInspection(await inspectRepo(bearer(), repoUrl.trim()));
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setInspecting(false);
    }
  };

  const toDetails = () => {
    if (inspection?.repoName && !name) setName(inspection.repoName);
    setStep('details');
  };

  const create = async (e: FormEvent) => {
    e.preventDefault();
    if (!name.trim()) {
      setError('Name is required.');
      return;
    }
    setBusy(true);
    setError('');
    try {
      await createPrototype(bearer(), {
        name: name.trim(),
        type: type as string,
        description: description.trim() || undefined,
        githubRepoUrl: type === 'functional' ? repoUrl.trim() : undefined,
        githubBranch: type === 'functional' ? inspection?.defaultBranch ?? undefined : undefined,
        language: type === 'functional' ? inspection?.detectedLanguage ?? undefined : undefined,
      });
      reset();
      onCreated();
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="wizard">
      <div className="wizard-steps muted">
        <span className={step === 'type' ? 'on' : ''}>1 · Type</span>
        {type === 'functional' && <span className={step === 'repo' ? 'on' : ''}>2 · Repo</span>}
        <span className={step === 'details' ? 'on' : ''}>{type === 'functional' ? '3' : '2'} · Details</span>
      </div>

      {step === 'type' && (
        <div className="choices">
          <button type="button" className="choice" onClick={() => chooseType('illustrative')}>
            <strong>Illustrative</strong>
            <span className="muted">Static mockup images with click-through hotspots.</span>
          </button>
          <button type="button" className="choice" onClick={() => chooseType('functional')}>
            <strong>Functional</strong>
            <span className="muted">A self-contained git repo Proto builds and runs.</span>
          </button>
        </div>
      )}

      {step === 'repo' && (
        <div>
          <div className="field">
            <label htmlFor="repoUrl">GitHub repo URL</label>
            <div className="inline">
              <input
                id="repoUrl"
                value={repoUrl}
                onChange={(e) => setRepoUrl(e.target.value)}
                placeholder="https://github.com/org/repo"
                disabled={inspecting}
              />
              <button type="button" onClick={inspect} disabled={inspecting || !repoUrl.trim()}>
                {inspecting ? 'Inspecting…' : 'Inspect'}
              </button>
            </div>
          </div>

          {inspection && !inspection.accessible && <p className="error">{inspection.message}</p>}
          {inspection?.accessible && !inspection.supported && (
            <p className="error">
              Detected <strong>{inspection.detectedLanguage ?? 'unknown'}</strong> — {inspection.message}
            </p>
          )}
          {inspection?.accessible && inspection.supported && (
            <div className="inspect-ok">
              <p>
                ✓ <strong>{inspection.detectedLanguage}</strong> · branch{' '}
                <span className="mono">{inspection.defaultBranch}</span>
              </p>
              <p className="muted">This prototype must meet the contract:</p>
              <ul className="constraint-list">
                {CONSTRAINTS.map((c) => (
                  <li key={c}>{c}</li>
                ))}
              </ul>
              <label className="checkbox">
                <input type="checkbox" checked={ack} onChange={(e) => setAck(e.target.checked)} />
                My repo meets these constraints.
              </label>
            </div>
          )}

          <div className="wizard-actions">
            <button type="button" className="link" onClick={() => setStep('type')}>
              Back
            </button>
            <button
              type="button"
              onClick={toDetails}
              disabled={!inspection?.supported || !ack}
            >
              Continue
            </button>
          </div>
        </div>
      )}

      {step === 'details' && (
        <form onSubmit={create}>
          <div className="field">
            <label htmlFor="pName">Name</label>
            <input id="pName" value={name} onChange={(e) => setName(e.target.value)} placeholder="Checkout redesign" disabled={busy} />
          </div>
          <div className="field">
            <label htmlFor="pDesc">Description (optional)</label>
            <input id="pDesc" value={description} onChange={(e) => setDescription(e.target.value)} disabled={busy} />
          </div>
          {type === 'functional' && inspection && (
            <p className="muted">
              {inspection.detectedLanguage} · {inspection.repoName} · {inspection.defaultBranch}
            </p>
          )}
          <div className="wizard-actions">
            <button type="button" className="link" onClick={() => setStep(type === 'functional' ? 'repo' : 'type')}>
              Back
            </button>
            <button type="submit" disabled={busy}>
              {busy ? 'Creating…' : 'Create prototype'}
            </button>
          </div>
        </form>
      )}

      {error && <p className="error">{error}</p>}
    </div>
  );
}
