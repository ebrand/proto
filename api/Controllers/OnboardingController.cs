using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Proto.Api.Models;
using Proto.Api.Options;
using Proto.Api.Services;

namespace Proto.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class OnboardingController(
    IOptions<StytchOptions> stytch,
    IOptions<SupabaseOptions> supabase,
    ILogger<OnboardingController> logger) : ControllerBase
{
    /// <summary>
    /// Flow 2 — new signup. Exchanges the browser's intermediate session token
    /// for a new Stytch org (chosen name + locked JIT), writes the tenants +
    /// admin users rows atomically, and returns session tokens for the browser
    /// to adopt via <c>session.updateSession(...)</c> + <c>session.authenticate()</c>.
    /// </summary>
    [HttpPost("signup")]
    public async Task<IActionResult> Signup([FromBody] SignupRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.IntermediateSessionToken)
            || string.IsNullOrWhiteSpace(request.OrganizationName)
            || string.IsNullOrWhiteSpace(request.TierCode))
        {
            return BadRequest(new { error = "missing_fields" });
        }

        if (!stytch.Value.IsConfigured || !supabase.Value.IsConfigured)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "not_configured" });
        }

        // Resolve lazily so the 503 guard above short-circuits cleanly when the
        // DB isn't configured (avoids constructing the Npgsql data source).
        var provisioning = HttpContext.RequestServices.GetRequiredService<TenantProvisioningService>();

        try
        {
            var session = await provisioning.SignupAsync(request, ct);
            return Ok(session);
        }
        catch (ProvisioningException ex)
        {
            // Org was created in Stytch but the DB write failed — orphaned org.
            // TODO: compensating delete of the Stytch org (tracked in the design).
            logger.LogError(ex, "DB provisioning failed after Stytch org creation");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "provisioning_failed", detail = ex.Message });
        }
        catch (Stytch.net.Exceptions.StytchApiException ex)
        {
            // A structured Stytch API error. A 4xx (e.g. an invalid/expired IST)
            // is the caller's problem -> surface it as a 400 with Stytch's own
            // error_type; treat anything else as an upstream 502.
            logger.LogWarning(ex, "Stytch API error: {ErrorType}", ex.ErrorType);
            var clientError = ex.StatusCode is >= 400 and < 500;
            return StatusCode(
                clientError ? StatusCodes.Status400BadRequest : StatusCodes.Status502BadGateway,
                new { error = "stytch_error", type = ex.ErrorType, detail = ex.ErrorMessage });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected failure during signup");
            return StatusCode(StatusCodes.Status502BadGateway,
                new { error = "unexpected", detail = ex.Message });
        }
    }
}
