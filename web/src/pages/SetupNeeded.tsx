// Shown when VITE_STYTCH_PUBLIC_TOKEN is not set, so the dev server still boots
// with a useful message instead of a blank crash.
export function SetupNeeded() {
  return (
    <div className="auth-shell">
      <div className="auth-card">
        <h1>Proto</h1>
        <p className="muted">Stytch is not configured yet.</p>
        <ol className="setup-steps">
          <li>
            Copy <code>.env.example</code> to <code>.env.local</code>.
          </li>
          <li>
            Set <code>VITE_STYTCH_PUBLIC_TOKEN</code> to your Stytch B2B public
            token.
          </li>
          <li>Restart the dev server.</li>
        </ol>
      </div>
    </div>
  );
}
