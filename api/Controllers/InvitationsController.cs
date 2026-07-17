using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Proto.Api.Auth;
using Proto.Api.Models;
using Proto.Api.Options;
using Proto.Api.Services;

namespace Proto.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(AuthenticationSchemes = StytchAuth.Scheme)]
public sealed class InvitationsController(
    IOptions<StytchOptions> stytch,
    IOptions<SupabaseOptions> supabase,
    ILogger<InvitationsController> logger) : ControllerBase
{
    private static readonly string[] Roles = ["admin", "member"];

    /// <summary>
    /// Flow 1 — an admin invites an email into their tenant. Requires the
    /// caller to be an admin of the tenant that owns their session's org.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Invite([FromBody] InviteRequest request, CancellationToken ct)
    {
        if (!stytch.Value.IsConfigured || !supabase.Value.IsConfigured)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "not_configured" });
        }

        var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        var role = request.TenantRole?.Trim().ToLowerInvariant() ?? string.Empty;
        logger.LogInformation("Invite request: email='{Email}' role='{Role}'", email, role);
        if (email.Length == 0 || !email.Contains('@'))
        {
            return BadRequest(new { error = "invalid_email", detail = $"email='{email}'" });
        }
        if (!Roles.Contains(role))
        {
            return BadRequest(new { error = "invalid_role", detail = $"role='{role}' (expected admin|member)" });
        }

        var memberId = User.FindFirstValue(StytchAuth.MemberIdClaim);
        var orgId = User.FindFirstValue(StytchAuth.OrganizationIdClaim);
        var sessionToken = BearerToken();
        if (string.IsNullOrEmpty(memberId) || string.IsNullOrEmpty(orgId) || sessionToken.Length == 0)
        {
            return Unauthorized();
        }

        var repo = HttpContext.RequestServices.GetRequiredService<TenantRepository>();
        var admin = await repo.FindMemberContextAsync(memberId, ct);
        if (admin is null)
        {
            return NotFound(new { error = "not_provisioned" });
        }
        if (admin.User.TenantRole != "admin")
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "not_admin" });
        }

        var invites = HttpContext.RequestServices.GetRequiredService<InvitationsService>();
        try
        {
            await invites.InviteAsync(Guid.Parse(admin.Tenant.Id), orgId, email, role, memberId, sessionToken, ct);
            return Ok(new { email, status = "invited" });
        }
        catch (SeatLimitException ex)
        {
            return Conflict(new { error = "seat_limit", detail = ex.Message });
        }
        catch (ProvisioningException ex)
        {
            return Conflict(new { error = "conflict", detail = ex.Message });
        }
        catch (Stytch.net.Exceptions.StytchApiException ex)
        {
            logger.LogWarning(ex, "Stytch invite failed: {ErrorType}", ex.ErrorType);
            var clientError = ex.StatusCode is >= 400 and < 500;
            return StatusCode(
                clientError ? StatusCodes.Status400BadRequest : StatusCodes.Status502BadGateway,
                new { error = "stytch_error", type = ex.ErrorType, detail = ex.ErrorMessage });
        }
    }

    /// <summary>
    /// Called by the frontend after the invitee authenticates the magic link.
    /// Binds the now-active member to their pending users row. Idempotent.
    /// </summary>
    [HttpPost("accept")]
    public async Task<IActionResult> Accept(CancellationToken ct)
    {
        if (!supabase.Value.IsConfigured)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "not_configured" });
        }

        var memberId = User.FindFirstValue(StytchAuth.MemberIdClaim);
        var orgId = User.FindFirstValue(StytchAuth.OrganizationIdClaim);
        var email = User.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
        var name = User.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
        if (string.IsNullOrEmpty(memberId) || string.IsNullOrEmpty(orgId))
        {
            return Unauthorized();
        }

        var repo = HttpContext.RequestServices.GetRequiredService<TenantRepository>();
        // Activate the pending row, or fall back to the already-active context.
        var me = await repo.ActivateInviteAsync(memberId, orgId, email, name, ct)
                 ?? await repo.FindMemberContextAsync(memberId, ct);
        if (me is null)
        {
            return NotFound(new { error = "not_provisioned" });
        }
        return Ok(me);
    }

    private string BearerToken()
    {
        var header = Request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : string.Empty;
    }
}
