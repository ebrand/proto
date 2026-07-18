using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Proto.Api.Auth;
using Proto.Api.Models;
using Proto.Api.Options;
using Proto.Api.Services;

namespace Proto.Api.Controllers;

// Page-image upload for illustrative pages. The API is the trust boundary: it
// issues a signed upload URL (bytes go browser -> Supabase Storage directly),
// then records the image on the page when the browser confirms the upload.
[ApiController]
[Route("api/prototypes/{prototypeId}/pages/{pageId}/image")]
[Authorize(AuthenticationSchemes = StytchAuth.Scheme)]
public sealed class PageImagesController(
    IOptions<SupabaseOptions> supabase,
    SupabaseStorageClient storage,
    ILogger<PageImagesController> logger) : ControllerBase
{
    // content-type -> file extension (also the accepted set, mirroring the bucket policy).
    private static readonly IReadOnlyDictionary<string, string> Extensions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/png"] = "png",
            ["image/jpeg"] = "jpg",
            ["image/webp"] = "webp",
        };

    /// <summary>Issue a signed URL the browser PUTs the image bytes to.</summary>
    [HttpPost("upload-url")]
    public async Task<IActionResult> UploadUrl(
        string prototypeId, string pageId,
        [FromBody] UploadUrlRequest request, CancellationToken ct)
    {
        if (!storage.IsConfigured)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "storage_not_configured" });
        if (!Guid.TryParse(pageId, out var pgId)) return NotFound();
        if (request.ContentType is null || !Extensions.TryGetValue(request.ContentType, out var ext))
            return BadRequest(new { error = "unsupported_type", detail = "png | jpeg | webp" });

        var (tenantId, protoId, guard) = await ResolveScopeAsync(prototypeId, ct);
        if (guard is not null) return guard;

        var pages = HttpContext.RequestServices.GetRequiredService<UxPageRepository>();
        if (!await pages.PageBelongsAsync(tenantId, protoId, pgId, ct))
            return NotFound(new { error = "page_not_found" });

        // Path binds the object to this tenant + page; a fresh guid avoids
        // collisions and lets a re-upload replace cleanly.
        var path = $"{tenantId}/{pgId}/{Guid.NewGuid():n}.{ext}";
        var (uploadUrl, token) = await storage.CreateSignedUploadUrlAsync(path, ct);

        return Ok(new UploadUrlResponse(uploadUrl, token, path, storage.PublicUrl(path)));
    }

    /// <summary>Record the uploaded image on the page.</summary>
    [HttpPut]
    public async Task<IActionResult> Confirm(
        string prototypeId, string pageId,
        [FromBody] ConfirmImageRequest request, CancellationToken ct)
    {
        if (!Guid.TryParse(pageId, out var pgId)) return NotFound();
        if (request.Width <= 0 || request.Height <= 0)
            return BadRequest(new { error = "invalid_dimensions" });

        var (tenantId, protoId, guard) = await ResolveScopeAsync(prototypeId, ct);
        if (guard is not null) return guard;

        // The path must be the one we issued for this tenant + page — don't let a
        // client point the row at an arbitrary object.
        var prefix = $"{tenantId}/{pgId}/";
        if (string.IsNullOrWhiteSpace(request.Path) || !request.Path.StartsWith(prefix, StringComparison.Ordinal))
            return BadRequest(new { error = "invalid_path" });

        var pages = HttpContext.RequestServices.GetRequiredService<UxPageRepository>();
        var ok = await pages.SetImageAsync(
            tenantId, protoId, pgId, request.Path, storage.PublicUrl(request.Path),
            request.Width, request.Height, ct);
        if (!ok) return NotFound(new { error = "page_not_found" });

        logger.LogInformation("Set image on page {PageId}", pgId);
        return Ok(new { imageUrl = storage.PublicUrl(request.Path) });
    }

    /// <summary>Remove a page's image (row + storage object).</summary>
    [HttpDelete]
    public async Task<IActionResult> Delete(string prototypeId, string pageId, CancellationToken ct)
    {
        if (!Guid.TryParse(pageId, out var pgId)) return NotFound();

        var (tenantId, protoId, guard) = await ResolveScopeAsync(prototypeId, ct);
        if (guard is not null) return guard;

        var pages = HttpContext.RequestServices.GetRequiredService<UxPageRepository>();
        var oldPath = await pages.ClearImageAsync(tenantId, protoId, pgId, ct);
        if (oldPath is null) return NoContent(); // nothing to clear (or no image)

        // Best-effort object cleanup — the row is already clean.
        if (storage.IsConfigured)
        {
            var deleted = await storage.DeleteAsync(oldPath, ct);
            if (!deleted) logger.LogWarning("Orphaned storage object {Path}", oldPath);
        }
        return NoContent();
    }

    /// <summary>Resolve caller -> tenant and confirm the prototype is theirs.</summary>
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
