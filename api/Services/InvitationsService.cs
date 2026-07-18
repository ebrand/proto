using Stytch.net.Clients;
using Stytch.net.Models;

namespace Proto.Api.Services;

/// <summary>
/// Flow 1 (invite). Reserves a seat + pending users row, creates the pending
/// Stytch member WITHOUT triggering Stytch's email, then sends our own invite
/// notification via Resend. The invitee accepts by signing in with Google — the
/// org appears in their discovery. If any step fails, everything is rolled back
/// so a failed invite leaves no residue.
/// </summary>
public sealed class InvitationsService(
    B2BClient stytch,
    TenantRepository tenants,
    ResendClient resend,
    IConfiguration config,
    ILogger<InvitationsService> logger)
{
    public async Task InviteAsync(
        Guid tenantId,
        string stytchOrgId,
        string tenantName,
        string email,
        string tenantRole,
        CancellationToken ct)
    {
        // 1. Reserve the seat (seat-checked, transactional).
        var pendingUserId = await tenants.CreatePendingInviteAsync(tenantId, email, tenantRole, ct);

        string? createdMemberId = null;
        try
        {
            // 2. Create the pending Stytch member — no email sent by Stytch.
            var createRequest = new B2BOrganizationsMembersCreateRequest(stytchOrgId, email)
            {
                CreateMemberAsPending = true,
                Roles = tenantRole == "admin" ? new List<string> { "stytch_admin" } : null,
            };
            var created = await stytch.Organizations.Members.Create(
                createRequest, new B2BOrganizationsMembersCreateRequestOptions());
            createdMemberId = created.MemberId;

            // 3. Send our own invite notification via Resend.
            var appUrl = config["Web:BaseUrl"] ?? "http://localhost:5173";
            await resend.SendInviteEmailAsync(email, tenantName, appUrl, ct);

            logger.LogInformation("Invited {Email} to org {OrgId} (member {MemberId})",
                email, stytchOrgId, createdMemberId);
        }
        catch
        {
            // Roll back whatever succeeded, in reverse order.
            if (createdMemberId is not null)
            {
                try
                {
                    await stytch.Organizations.Members.Delete(
                        new B2BOrganizationsMembersDeleteRequest(stytchOrgId, createdMemberId),
                        new B2BOrganizationsMembersDeleteRequestOptions());
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to roll back Stytch member {MemberId}", createdMemberId);
                }
            }
            await tenants.DeleteUserAsync(pendingUserId, ct);
            throw;
        }
    }
}
