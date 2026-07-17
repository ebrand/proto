import {
  AuthFlowType,
  B2BProducts,
  B2BOAuthProviders,
  type StytchB2BUIConfig,
} from '@stytch/react/b2b';

// Where Stytch sends the browser back after the Google round-trip. This exact
// URL must also be registered as a Redirect URL in the Stytch dashboard, or the
// callback is rejected. The /authenticate route mounts the same StytchB2B
// component, which detects the token in the URL and completes the discovery flow
// (org selection / creation -> full member session).
const redirectURL = `${window.location.origin}/authenticate`;

// Discovery flow: authenticate with Google first, then resolve which
// Organization (our Tenant) the member is logging into. Google is the only
// provider enabled for now; add more to the providers array later.
export const authConfig: StytchB2BUIConfig = {
  authFlowType: AuthFlowType.Discovery,
  products: [B2BProducts.oauth],
  sessionOptions: {
    // 1 hour. Min 5, max 525600 (365 days) per the SDK.
    sessionDurationMinutes: 60,
  },
  oauthOptions: {
    providers: [{ type: B2BOAuthProviders.Google }],
    loginRedirectURL: redirectURL,
    signupRedirectURL: redirectURL,
    discoveryRedirectURL: redirectURL,
  },
};
