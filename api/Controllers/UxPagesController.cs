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
[Route("api/prototypes/{prototypeId}/pages")]
[Authorize(AuthenticationSchemes = StytchAuth.Scheme)]
public sealed class UxPagesController(
    IOptions<SupabaseOptions> supabase,
    ILogger<UxPagesController> logger) : ControllerBase
{
    private static readonly string[] Kinds = ["illustrative", "functional"];

    /// <summary>List the pages of a prototype (must belong to the caller's tenant).</summary>
    [HttpGet]
    public async Task<IActionResult> List(string prototypeId, CancellationToken ct)
    {
        var (tenantId, protoId, guard) = await ResolveScopeAsync(prototypeId, ct);
        if (guard is not null) return guard;

        var pages = HttpContext.RequestServices.GetRequiredService<UxPageRepository>();
        return Ok(await pages.ListAsync(tenantId, protoId, ct));
    }

    /// <summary>Add a page to a prototype in the caller's tenant.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        string prototypeId, [FromBody] CreateUxPageRequest request, CancellationToken ct)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        var kind = request.Kind?.Trim().ToLowerInvariant() ?? string.Empty;
        if (name.Length == 0) return BadRequest(new { error = "name_required" });
        if (!Kinds.Contains(kind)) return BadRequest(new { error = "invalid_kind", detail = "expected illustrative|functional" });

        var (tenantId, protoId, guard) = await ResolveScopeAsync(prototypeId, ct);
        if (guard is not null) return guard;

        var pages = HttpContext.RequestServices.GetRequiredService<UxPageRepository>();
        var id = await pages.CreateAsync(
            tenantId, protoId, name, kind,
            request.OrderIndex ?? 0,
            request.IsEntryPage ?? false,
            string.IsNullOrWhiteSpace(request.Route) ? null : request.Route.Trim(),
            ct);

        logger.LogInformation("Created page {Id} on prototype {ProtoId}", id, protoId);
        return Created($"/api/prototypes/{protoId}/pages/{id}", new { id });
    }

    /// <summary>Persist a page's position on the flow-map canvas (drag-to-move).</summary>
    [HttpPut("{pageId}/position")]
    public async Task<IActionResult> UpdatePosition(
        string prototypeId, string pageId,
        [FromBody] UpdatePagePositionRequest request, CancellationToken ct)
    {
        if (!Guid.TryParse(pageId, out var pgId)) return NotFound();
        if (double.IsNaN(request.X) || double.IsNaN(request.Y) ||
            double.IsInfinity(request.X) || double.IsInfinity(request.Y))
        {
            return BadRequest(new { error = "invalid_position" });
        }

        var (tenantId, protoId, guard) = await ResolveScopeAsync(prototypeId, ct);
        if (guard is not null) return guard;

        var pages = HttpContext.RequestServices.GetRequiredService<UxPageRepository>();
        var moved = await pages.UpdatePositionAsync(tenantId, protoId, pgId, request.X, request.Y, ct);
        if (!moved) return NotFound(new { error = "page_not_found" });

        return NoContent();
    }

    /// <summary>
    /// Resolve the caller to their tenant and confirm the prototype belongs to
    /// it. Returns (tenantId, prototypeId, null) on success, or (default,
    /// default, errorResult) with a ready-to-return response on failure.
    /// </summary>
    private async Task<(Guid TenantId, Guid PrototypeId, IActionResult? Guard)> ResolveScopeAsync(
        string prototypeId, CancellationToken ct)
    {
        if (!supabase.Value.IsConfigured)
        {
            return (default, default, StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "not_configured" }));
        }
        if (!Guid.TryParse(prototypeId, out var protoId))
        {
            return (default, default, NotFound());
        }

        var memberId = User.FindFirstValue(StytchAuth.MemberIdClaim);
        if (string.IsNullOrEmpty(memberId))
        {
            return (default, default, Unauthorized());
        }

        var tenants = HttpContext.RequestServices.GetRequiredService<TenantRepository>();
        var me = await tenants.FindMemberContextAsync(memberId, ct);
        if (me is null)
        {
            return (default, default, NotFound(new { error = "not_provisioned" }));
        }

        var tenantId = Guid.Parse(me.Tenant.Id);
        var prototypes = HttpContext.RequestServices.GetRequiredService<PrototypeRepository>();
        var proto = await prototypes.GetAsync(tenantId, protoId, ct);
        if (proto is null)
        {
            return (default, default, NotFound(new { error = "prototype_not_found" }));
        }

        return (tenantId, protoId, null);
    }
}
