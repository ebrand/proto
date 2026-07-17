using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Proto.Api.Auth;
using Proto.Api.Options;
using Proto.Api.Services;

namespace Proto.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(AuthenticationSchemes = StytchAuth.Scheme)]
public sealed class MeController(
    IOptions<SupabaseOptions> supabase,
    ILogger<MeController> logger) : ControllerBase
{
    /// <summary>
    /// Resolve the current caller (validated Stytch session → member id) to
    /// their Proto user + tenant. 404 <c>not_provisioned</c> means the session
    /// is valid but no Proto rows exist for it (e.g. an unprovisioned org).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!supabase.Value.IsConfigured)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "not_configured" });
        }

        var memberId = User.FindFirstValue(StytchAuth.MemberIdClaim);
        if (string.IsNullOrEmpty(memberId))
        {
            // Authenticated but no member-id claim — shouldn't happen.
            return Unauthorized();
        }

        var repo = HttpContext.RequestServices.GetRequiredService<TenantRepository>();
        var me = await repo.FindMemberContextAsync(memberId, ct);
        if (me is null)
        {
            logger.LogInformation("Authenticated member {MemberId} has no Proto rows", memberId);
            return NotFound(new { error = "not_provisioned" });
        }

        return Ok(me);
    }
}
