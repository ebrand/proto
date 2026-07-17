using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Proto.Api.Options;

namespace Proto.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class HealthController(
    IOptions<StytchOptions> stytch,
    IOptions<SupabaseOptions> supabase) : ControllerBase
{
    /// <summary>
    /// Liveness + configuration probe. Reports whether the Stytch and Supabase
    /// credentials are present WITHOUT echoing any secret value.
    /// </summary>
    [HttpGet]
    public IActionResult Get() => Ok(new
    {
        status = "ok",
        stytchConfigured = stytch.Value.IsConfigured,
        supabaseConfigured = supabase.Value.IsConfigured,
    });
}
