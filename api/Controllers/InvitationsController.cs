using Microsoft.AspNetCore.Mvc;
using Proto.Api.Models;

namespace Proto.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class InvitationsController : ControllerBase
{
    /// <summary>
    /// Flow 1 — invite a member into the caller's tenant. Requires an
    /// authenticated admin session (validation pending). Enforces the tier seat
    /// limit, then sends a Stytch invite scoped to the admin's org.
    /// </summary>
    /// <remarks>
    /// STUB. Implementation pending:
    ///  1. Validate the caller's Stytch session -> member_id + organization_id;
    ///     confirm tenant_role = admin.
    ///  2. Seat check: count users in the tenant vs subscription_tiers.max_seats.
    ///  3. Stytch: magic_links/email/invite scoped to the org, invite_redirect_url
    ///     -> the callback endpoint below.
    ///  4. Optionally insert a pending users row (status invited).
    /// </remarks>
    [HttpPost]
    public IActionResult Invite([FromBody] InviteRequest request)
        => StatusCode(StatusCodes.Status501NotImplemented,
            new { error = "not_implemented", flow = "invite" });

    /// <summary>
    /// invite_redirect_url target. Stytch sends the invited, now-authenticated
    /// member here; we finalize their users row under the target tenant.
    /// </summary>
    /// <remarks>STUB — authenticate the Stytch token, then upsert the users row.</remarks>
    [HttpGet("callback")]
    public IActionResult Callback()
        => StatusCode(StatusCodes.Status501NotImplemented,
            new { error = "not_implemented", flow = "invite_callback" });
}
