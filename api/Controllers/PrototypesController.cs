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
public sealed class PrototypesController(
    IOptions<SupabaseOptions> supabase,
    ILogger<PrototypesController> logger) : ControllerBase
{
    private static readonly string[] Types = ["functional", "illustrative"];

    /// <summary>List the caller's tenant's prototypes.</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (!supabase.Value.IsConfigured)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "not_configured" });
        }
        var me = await ResolveContextAsync(ct);
        if (me is null) return NotFound(new { error = "not_provisioned" });

        var repo = HttpContext.RequestServices.GetRequiredService<PrototypeRepository>();
        var items = await repo.ListAsync(Guid.Parse(me.Tenant.Id), ct);
        return Ok(items);
    }

    /// <summary>Create a prototype in the caller's tenant; the caller becomes the owner.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePrototypeRequest request, CancellationToken ct)
    {
        if (!supabase.Value.IsConfigured)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "not_configured" });
        }

        var name = request.Name?.Trim() ?? string.Empty;
        var type = request.Type?.Trim().ToLowerInvariant() ?? string.Empty;
        var repoUrl = string.IsNullOrWhiteSpace(request.GithubRepoUrl) ? null : request.GithubRepoUrl.Trim();
        if (name.Length == 0) return BadRequest(new { error = "name_required" });
        if (!Types.Contains(type)) return BadRequest(new { error = "invalid_type", detail = "expected functional|illustrative" });
        if (type == "functional" && repoUrl is null)
        {
            return BadRequest(new { error = "repo_required", detail = "functional prototypes need a github repo url" });
        }

        var me = await ResolveContextAsync(ct);
        if (me is null) return NotFound(new { error = "not_provisioned" });

        var repo = HttpContext.RequestServices.GetRequiredService<PrototypeRepository>();
        var id = await repo.CreateAsync(
            Guid.Parse(me.Tenant.Id), Guid.Parse(me.User.Id), name, type,
            string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            repoUrl,
            string.IsNullOrWhiteSpace(request.GithubBranch) ? null : request.GithubBranch.Trim(),
            ct);

        logger.LogInformation("Created prototype {Id} in tenant {TenantId}", id, me.Tenant.Id);
        return CreatedAtAction(nameof(Get), new { id }, new { id });
    }

    /// <summary>Get one prototype (only if it belongs to the caller's tenant).</summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        if (!supabase.Value.IsConfigured)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "not_configured" });
        }
        if (!Guid.TryParse(id, out var prototypeId)) return NotFound();

        var me = await ResolveContextAsync(ct);
        if (me is null) return NotFound(new { error = "not_provisioned" });

        var repo = HttpContext.RequestServices.GetRequiredService<PrototypeRepository>();
        var proto = await repo.GetAsync(Guid.Parse(me.Tenant.Id), prototypeId, ct);
        return proto is null ? NotFound() : Ok(proto);
    }

    /// <summary>Resolve the validated session to the caller's Proto user + tenant.</summary>
    private async Task<MeResponse?> ResolveContextAsync(CancellationToken ct)
    {
        var memberId = User.FindFirstValue(StytchAuth.MemberIdClaim);
        if (string.IsNullOrEmpty(memberId)) return null;
        var tenants = HttpContext.RequestServices.GetRequiredService<TenantRepository>();
        return await tenants.FindMemberContextAsync(memberId, ct);
    }
}
