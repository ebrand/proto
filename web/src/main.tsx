import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { StytchB2BProvider } from '@stytch/react/b2b';
import './index.css';
import App from './App.tsx';
import { stytch } from './lib/stytch';
import { SetupNeeded } from './pages/SetupNeeded';

const root = createRoot(document.getElementById('root')!);

// Without a public token there is no client, so skip the provider entirely and
// show setup instructions rather than letting the SDK throw.
root.render(
  <StrictMode>
    {stytch ? (
      <StytchB2BProvider stytch={stytch}>
        <BrowserRouter>
          <App />
        </BrowserRouter>
      </StytchB2BProvider>
    ) : (
      <SetupNeeded />
    )}
  </StrictMode>,
);
