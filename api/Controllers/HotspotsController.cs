using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Proto.Api.Auth;
using Proto.Api.Models;
using Proto.Api.Options;
using Proto.Api.Services;

namespace Proto.Api.Controllers;

// Hotspots are the flow-map links. Read is prototype-wide (all arrows on the
// canvas); create/delete hang off a specific page. A link points at either
// another page (target_page_id — the arrow) or an external URL, never both.
[ApiController]
[Route("api/prototypes/{prototypeId}")]
[Authorize(AuthenticationSchemes = StytchAuth.Scheme)]
public sealed class HotspotsController(
    IOptions<SupabaseOptions> supabase,
    ILogger<HotspotsController> logger) : ControllerBase
{
    /// <summary>All hotspots across the prototype's pages (the flow-map arrows).</summary>
    [HttpGet("hotspots")]
    public async Task<IActionResult> List(string prototypeId, CancellationToken ct)
    {
        var (tenantId, protoId, guard) = await ResolveScopeAsync(prototypeId, ct);
        if (guard is not null) return guard;

        var hotspots = HttpContext.RequestServices.GetRequiredService<HotspotRepository>();
        return Ok(await hotspots.ListByPrototypeAsync(tenantId, protoId, ct));
    }

    /// <summary>Add a link/CTA on a page, pointing at another page or a URL.</summary>
    [HttpPost("pages/{pageId}/hotspots")]
    public async Task<IActionResult> Create(
        string prototypeId, string pageId,
        [FromBody] CreateHotspotRequest request, CancellationToken ct)
    {
        if (!Guid.TryParse(pageId, out var pgId)) return NotFound();

        // Exactly one destination.
        var hasTargetPage = !string.IsNullOrWhiteSpace(request.TargetPageId);
        var hasUrl = !string.IsNullOrWhiteSpace(request.TargetExternalUrl);
        if (hasTargetPage == hasUrl)
        {
            return BadRequest(new { error = "one_target_required", detail = "set exactly one of targetPageId | targetExternalUrl" });
        }

        Guid? targetPageId = null;
        if (hasTargetPage)
        {
            if (!Guid.TryParse(request.TargetPageId, out var tp))
                return BadRequest(new { error = "invalid_target_page" });
            targetPageId = tp;
        }

        // Geometry defaults to the whole page (normalized 0..1) when unset —
        // fine for a page->page arrow before per-image region editing exists.
        var rectX = request.RectX ?? 0;
        var rectY = request.RectY ?? 0;
        var rectW = request.RectWidth ?? 1;
        var rectH = request.RectHeight ?? 1;
        if (rectX < 0 || rectY < 0 || rectW < 0 || rectH < 0)
        {
            return BadRequest(new { error = "invalid_rect", detail = "rect values must be >= 0" });
        }

        var (tenantId, protoId, guard) = await ResolveScopeAsync(prototypeId, ct);
        if (guard is not null) return guard;

        var label = string.IsNullOrWhiteSpace(request.Label) ? null : request.Label.Trim();
        var url = hasUrl ? request.TargetExternalUrl!.Trim() : null;

        var hotspots = HttpContext.RequestServices.GetRequiredService<HotspotRepository>();
        var id = await hotspots.CreateRectAsync(
            tenantId, protoId, pgId, label, rectX, rectY, rectW, rectH, targetPageId, url, ct);

        // Null id => the source or target page isn't in this tenant+prototype.
        if (id is null) return NotFound(new { error = "page_not_found" });

        logger.LogInformation("Created hotspot {Id} on page {PageId}", id, pgId);
        return Created($"/api/prototypes/{protoId}/pages/{pgId}/hotspots/{id}", new { id });
    }

    /// <summary>Delete a hotspot from a page.</summary>
    [HttpDelete("pages/{pageId}/hotspots/{hotspotId}")]
    public async Task<IActionResult> Delete(
        string prototypeId, string pageId, string hotspotId, CancellationToken ct)
    {
        if (!Guid.TryParse(hotspotId, out var hsId)) return NotFound();

        var (tenantId, protoId, guard) = await ResolveScopeAsync(prototypeId, ct);
        if (guard is not null) return guard;

        var hotspots = HttpContext.RequestServices.GetRequiredService<HotspotRepository>();
        var deleted = await hotspots.DeleteAsync(tenantId, protoId, hsId, ct);
        return deleted ? NoContent() : NotFound(new { error = "hotspot_not_found" });
    }

    /// <summary>
    /// Resolve the caller to their tenant and confirm the prototype belongs to
    /// it. Mirrors UxPagesController.ResolveScopeAsync.
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
