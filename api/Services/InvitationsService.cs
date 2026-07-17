using Stytch.net.Clients;
using Stytch.net.Models;

namespace Proto.Api.Services;

/// <summary>
/// Flow 1 (invite). Reserves a seat + pending users row, then sends the Stytch
/// email magic-link invite scoped to the org and authorized by the inviting
/// admin's own session (Stytch enforces the inviter's RBAC). If the Stytch
/// invite fails, the pending row is removed so a failed invite doesn't burn a
/// seat.
/// </summary>
public sealed class InvitationsService(
    B2BClient stytch,
    TenantRepository tenants,
    IConfiguration config,
    ILogger<InvitationsService> logger)
{
    public async Task InviteAsync(
        Guid tenantId,
        string stytchOrgId,
        string email,
        string tenantRole,
        string adminMemberId,
        string adminSessionToken,
        CancellationToken ct)
    {
        // 1. Reserve the seat (seat-checked, transactional).
        var pendingUserId = await tenants.CreatePendingInviteAsync(tenantId, email, tenantRole, ct);

        try
        {
            // 2. Send the Stytch invite, authorized by the admin's session.
            var webBase = config["Web:BaseUrl"] ?? "http://localhost:5173";
            var request = new B2BMagicLinksEmailInviteRequest(stytchOrgId, email)
            {
                InviteRedirectURL = $"{webBase.TrimEnd('/')}/authenticate",
                InvitedByMemberId = adminMemberId,
                // Only assign NON-default roles explicitly. stytch_member is the
                // implicit default (Stytch rejects assigning it); admins get
                // stytch_admin, members get the default by omission.
                Roles = tenantRole == "admin" ? new List<string> { "stytch_admin" } : null,
            };
            var options = new B2BMagicLinksEmailInviteRequestOptions
            {
                Authorization = IsJwt(adminSessionToken)
                    ? new Authorization { SessionJwt = adminSessionToken }
                    : new Authorization { SessionToken = adminSessionToken },
            };

            await stytch.MagicLinks.Email.Invite(request, options);
            logger.LogInformation("Invited {Email} to org {OrgId}", email, stytchOrgId);
        }
        catch
        {
            // Compensate: the invite didn't go out, so release the reserved seat.
            await tenants.DeleteUserAsync(pendingUserId, ct);
            throw;
        }
    }

    private static bool IsJwt(string token) => token.Count(c => c == '.') == 2;
}
