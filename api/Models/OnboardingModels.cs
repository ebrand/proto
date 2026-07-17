namespace Proto.Api.Models;

// Request/response contracts for the onboarding + invitation flows described in
// the "Tenant Provisioning & Onboarding (Design)" Confluence page. These are
// the wire shapes the React app posts to; the handlers are still stubs.

/// <summary>
/// Flow 2 (new signup). The browser has completed Google discovery OAuth and
/// holds an intermediate session token (IST); it posts the IST plus the chosen
/// tenant name and tier here to create the Stytch org + Proto tenant atomically.
/// </summary>
public sealed record SignupRequest(
    string IntermediateSessionToken,
    string OrganizationName,
    string TierCode);

/// <summary>
/// Session tokens returned after provisioning. The browser hydrates its Stytch
/// SDK with these via <c>session.updateSession(...)</c> then
/// <c>session.authenticate()</c>.
/// </summary>
public sealed record SessionResponse(
    string SessionToken,
    string SessionJwt,
    string TenantId,
    string OrganizationId);

/// <summary>
/// Flow 1 (invite). An authenticated tenant admin invites an email into their
/// tenant. The target org is derived from the caller's validated session.
/// </summary>
public sealed record InviteRequest(
    string Email,
    string TenantRole);
