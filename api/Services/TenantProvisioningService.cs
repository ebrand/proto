using System.Text;
using Proto.Api.Models;
using Stytch.net.Clients;
using Stytch.net.Models;

namespace Proto.Api.Services;

/// <summary>
/// Orchestrates Flow 2 (new signup): exchange the browser's intermediate
/// session token for a new Stytch organization (created with the chosen name +
/// locked isolation), then persist the tenant + admin user atomically.
/// </summary>
public sealed class TenantProvisioningService(
    B2BClient stytch,
    TenantRepository tenants,
    ILogger<TenantProvisioningService> logger)
{
    public async Task<SessionResponse> SignupAsync(SignupRequest request, CancellationToken ct)
    {
        // 1. Create the Stytch org from the IST. Name is the user's choice;
        //    JIT is locked off so no one can auto-join this tenant (see design).
        var createReq = new B2BDiscoveryOrganizationsCreateRequest(request.IntermediateSessionToken)
        {
            OrganizationName = request.OrganizationName,
            SessionDurationMinutes = 60,
            EmailJITProvisioning = "NOT_ALLOWED",
            SSOJITProvisioning = "NOT_ALLOWED",
            EmailAllowedDomains = [],
        };

        var created = await stytch.Discovery.Organizations.Create(createReq);
        var org = created.Organization;
        var member = created.Member;
        logger.LogInformation("Stytch org created: {OrgId}", org.OrganizationId);

        // display_name has a length>=1 DB check; Google usually supplies a name,
        // but fall back to the email if it is blank.
        var memberName = string.IsNullOrWhiteSpace(member.Name) ? member.EmailAddress : member.Name;
        var slug = Slugify(org.OrganizationSlug, fallback: org.OrganizationId);

        // 2. Persist tenant + admin user in one transaction.
        var (tenantId, userId) = await tenants.ProvisionTenantAsync(
            tierCode: request.TierCode,
            orgName: org.OrganizationName,
            orgSlug: slug,
            stytchOrgId: org.OrganizationId,
            memberEmail: member.EmailAddress,
            memberName: memberName,
            stytchMemberId: member.MemberId,
            ct: ct);
        logger.LogInformation("Provisioned tenant {TenantId} / user {UserId}", tenantId, userId);

        // 3. Return the Stytch session for the browser to adopt via
        //    session.updateSession(...) + session.authenticate().
        return new SessionResponse(created.SessionToken, created.SessionJwt, tenantId, org.OrganizationId);
    }

    /// <summary>
    /// Coerce a Stytch slug into the tenants.slug shape (^[a-z0-9-]+$). Stytch
    /// slugs already fit, so this is mostly a safety net; falls back to a
    /// sanitized org id if the input reduces to empty.
    /// </summary>
    private static string Slugify(string? input, string fallback)
    {
        var source = string.IsNullOrWhiteSpace(input) ? fallback : input;
        var sb = new StringBuilder(source.Length);
        var lastHyphen = false;
        foreach (var ch in source.ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                sb.Append(ch);
                lastHyphen = false;
            }
            else if (!lastHyphen && sb.Length > 0)
            {
                sb.Append('-');
                lastHyphen = true;
            }
        }
        var result = sb.ToString().Trim('-');
        return result.Length > 0 ? result : "tenant";
    }
}
