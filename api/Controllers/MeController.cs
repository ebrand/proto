using Microsoft.AspNetCore.Mvc;

namespace Proto.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class MeController : ControllerBase
{
    /// <summary>
    /// Resolve the current caller (validated Stytch session -> member_id +
    /// organization_id) to their Proto users + tenants rows.
    /// </summary>
    /// <remarks>
    /// STUB. Implementation pending: read the session token from the request,
    /// validate via Stytch sessions/authenticate, map the sync keys to the
    /// users + tenants rows via the service-role DB connection.
    /// </remarks>
    [HttpGet]
    public IActionResult Get()
        => StatusCode(StatusCodes.Status501NotImplemented,
            new { error = "not_implemented", flow = "me" });
}
