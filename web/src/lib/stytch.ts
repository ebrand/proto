import { createStytchB2BClient } from '@stytch/react/b2b';

// Frontend (public) token from the Stytch dashboard. Public tokens are safe to
// ship to the browser; they only permit the flows enabled for the project.
export const stytchPublicToken = import.meta.env.VITE_STYTCH_PUBLIC_TOKEN;

// Create the client once, at module load. Null when the token is unset so the
// app can render a setup screen instead of crashing on a missing credential.
export const stytch = stytchPublicToken
  ? createStytchB2BClient(stytchPublicToken)
  : null;
