using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Stytch.net.Clients;
using Stytch.net.Models;

namespace Proto.Api.Auth;

/// <summary>Scheme name + claim types for Stytch-session authentication.</summary>
public static class StytchAuth
{
    public const string Scheme = "Stytch";
    public const string MemberIdClaim = "stytch_member_id";
    public const string OrganizationIdClaim = "stytch_organization_id";
}

/// <summary>
/// Validates a Stytch B2B session presented as <c>Authorization: Bearer
/// &lt;token&gt;</c>. The token may be the opaque session token or the session
/// JWT; both are accepted by Stytch's authenticate endpoint. On success the
/// caller's Stytch member id / organization id / email become claims.
///
/// This calls Stytch on every request (authoritative). A later optimization is
/// <c>Sessions.AuthenticateJwtLocal</c> (verify the JWT against Stytch's JWKS
/// with no network round-trip).
/// </summary>
public sealed class StytchSessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    B2BClient stytch)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header)
            || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var token = header["Bearer ".Length..].Trim();
        if (token.Length == 0) return AuthenticateResult.NoResult();

        var request = new B2BSessionsAuthenticateRequest();
        // A JWT has exactly two dots; an opaque session token does not.
        if (token.Count(c => c == '.') == 2) request.SessionJwt = token;
        else request.SessionToken = token;

        try
        {
            var result = await stytch.Sessions.Authenticate(request);
            var claims = new[]
            {
                new Claim(StytchAuth.MemberIdClaim, result.Member.MemberId),
                new Claim(StytchAuth.OrganizationIdClaim, result.Organization.OrganizationId),
                new Claim(ClaimTypes.Email, result.Member.EmailAddress ?? string.Empty),
                new Claim(ClaimTypes.Name, result.Member.Name ?? string.Empty),
            };
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, StytchAuth.Scheme));
            return AuthenticateResult.Success(new AuthenticationTicket(principal, StytchAuth.Scheme));
        }
        catch (Stytch.net.Exceptions.StytchApiException ex)
        {
            Logger.LogWarning("Stytch session authentication failed: {ErrorType}", ex.ErrorType);
            return AuthenticateResult.Fail(ex.ErrorMessage ?? "invalid session");
        }
    }
}
