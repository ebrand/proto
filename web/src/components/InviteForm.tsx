import { type FormEvent, useState } from 'react';
import { useStytchB2BClient } from '@stytch/react/b2b';
import { sendInvite } from '../lib/api';

// Admin-only: invite a teammate into the current tenant by email.
export function InviteForm() {
  const stytch = useStytchB2BClient();
  const [email, setEmail] = useState('');
  const [role, setRole] = useState('member');
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState('');
  const [error, setError] = useState('');

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setMessage('');
    setError('');
    try {
      const tokens = stytch.session.getTokens();
      const bearer = tokens?.session_jwt || tokens?.session_token;
      if (!bearer) throw new Error('Session token unavailable.');
      await sendInvite(bearer, email.trim(), role);
      setMessage(`Invite sent to ${email.trim()}.`);
      setEmail('');
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <form onSubmit={submit}>
      <div className="field">
        <label htmlFor="inviteEmail">Email</label>
        <input
          id="inviteEmail"
          type="email"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          placeholder="teammate@example.com"
          disabled={busy}
        />
      </div>
      <div className="field">
        <label htmlFor="inviteRole">Role</label>
        <select id="inviteRole" value={role} onChange={(e) => setRole(e.target.value)} disabled={busy}>
          <option value="member">Member</option>
          <option value="admin">Admin</option>
        </select>
      </div>
      {message && <p className="muted">{message}</p>}
      {error && <p className="error">{error}</p>}
      <button type="submit" disabled={busy}>
        {busy ? 'Sending…' : 'Send invite'}
      </button>
    </form>
  );
}
