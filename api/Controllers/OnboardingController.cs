using Microsoft.AspNetCore.Mvc;
using Proto.Api.Models;

namespace Proto.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class OnboardingController : ControllerBase
{
    /// <summary>
    /// Flow 2 — new signup. Exchanges the browser's intermediate session token
    /// for a new Stytch org (created with the chosen name + locked JIT
    /// settings), writes the tenants + admin users rows atomically, and returns
    /// session tokens for the browser to adopt.
    /// </summary>
    /// <remarks>
    /// STUB. Implementation pending:
    ///  1. Stytch: create-organization-via-discovery with the IST +
    ///     organization_name, email_jit_provisioning=NOT_ALLOWED,
    ///     sso_jit_provisioning=NOT_ALLOWED.
    ///  2. DB: insert tenants (name, subscription_tier_id from TierCode,
    ///     stytch_organization_id) + users (stytch_member_id, admin) in one tx.
    ///  3. Return SessionResponse from the Stytch create result.
    /// </remarks>
    [HttpPost("signup")]
    public IActionResult Signup([FromBody] SignupRequest request)
        => StatusCode(StatusCodes.Status501NotImplemented,
            new { error = "not_implemented", flow = "signup" });
}
