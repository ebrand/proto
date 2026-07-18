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
    IServiceScopeFactory scopeFactory,
    ILogger<PrototypesController> logger) : ControllerBase
{
    private static readonly string[] Types = ["functional", "illustrative"];

    /// <summary>Resolve a repo's current commit SHA (null on any failure).</summary>
    private static async Task<string?> ResolveShaAsync(IServiceProvider services, string repoUrl, string? branch, CancellationToken ct)
    {
        if (!GitHubClient.TryParse(repoUrl, out var owner, out var repo)) return null;
        var github = services.GetRequiredService<GitHubClient>();
        try { return await github.ResolveCommitAsync(owner, repo, branch, ct); }
        catch { return null; }
    }

    /// <summary>Start a build+deploy and record it as building (status/id/commit).</summary>
    private static async Task StartAndRecordAsync(
        IServiceProvider services, Guid tenantId, Guid prototypeId, string repoUrl, string? branch, string? sha, CancellationToken ct)
    {
        var runner = services.GetRequiredService<GcpRunnerService>();
        var repo = services.GetRequiredService<PrototypeRepository>();
        var buildId = await runner.StartBuildAsync(prototypeId.ToString(), repoUrl, branch, ct);
        await repo.SetBuildStartedAsync(
            tenantId, prototypeId, buildId, GcpRunnerService.ServiceNameFor(prototypeId.ToString()), sha, ct);
    }

    /// <summary>
    /// Inspect a GitHub repo for the define-a-prototype workflow: detect the
    /// language and whether it's supported. No tenant data touched.
    /// </summary>
    [HttpPost("inspect")]
    public async Task<IActionResult> Inspect([FromBody] RepoInspectRequest request, CancellationToken ct)
    {
        var github = HttpContext.RequestServices.GetRequiredService<GitHubClient>();
        var result = await github.InspectAsync(request.RepoUrl ?? string.Empty, ct);
        return Ok(result);
    }

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
            type == "functional" && !string.IsNullOrWhiteSpace(request.Language) ? request.Language.Trim() : null,
            ct);

        logger.LogInformation("Created prototype {Id} in tenant {TenantId}", id, me.Tenant.Id);

        // Warm it up: kick off the build in the background so it's ready (or
        // building) by the time the user opens it, and the layer cache is
        // populated. Detached from the request; failures degrade to a manual
        // Build & run.
        var runner = HttpContext.RequestServices.GetRequiredService<GcpRunnerService>();
        if (type == "functional" && repoUrl is not null && runner.IsConfigured)
        {
            var pid = Guid.Parse(id);
            var tenantId = Guid.Parse(me.Tenant.Id);
            var branch = string.IsNullOrWhiteSpace(request.GithubBranch) ? null : request.GithubBranch.Trim();
            _ = Task.Run(async () =>
            {
                using var scope = scopeFactory.CreateScope();
                try
                {
                    var sha = await ResolveShaAsync(scope.ServiceProvider, repoUrl, branch, CancellationToken.None);
                    await StartAndRecordAsync(scope.ServiceProvider, tenantId, pid, repoUrl, branch, sha, CancellationToken.None);
                }
                catch (Exception e)
                {
                    logger.LogWarning(e, "Auto-build on create failed for {Id}", pid);
                }
            });
        }

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

        var tenantId = Guid.Parse(me.Tenant.Id);
        var repo = HttpContext.RequestServices.GetRequiredService<PrototypeRepository>();
        var proto = await repo.GetAsync(tenantId, prototypeId, ct);
        if (proto is null) return NotFound();

        // Reconcile-on-read: if a build is in flight, poll Cloud Build once and
        // self-heal to ready+url / failed. No background worker to lose.
        if (proto.BuildStatus == "building")
        {
            var runner = HttpContext.RequestServices.GetRequiredService<GcpRunnerService>();
            var buildId = runner.IsConfigured ? await repo.GetBuildIdAsync(tenantId, prototypeId, ct) : null;
            if (buildId is not null)
            {
                try
                {
                    var (state, url, error) = await runner.CheckBuildAsync(prototypeId.ToString(), buildId, ct);
                    if (state == GcpRunnerService.BuildState.Ready)
                    {
                        await repo.SetBuildResultAsync(tenantId, prototypeId, "ready", url, null, ct);
                        proto = await repo.GetAsync(tenantId, prototypeId, ct);
                    }
                    else if (state == GcpRunnerService.BuildState.Failed)
                    {
                        await repo.SetBuildResultAsync(tenantId, prototypeId, "failed", null, error, ct);
                        proto = await repo.GetAsync(tenantId, prototypeId, ct);
                    }
                }
                catch (Exception e)
                {
                    logger.LogWarning(e, "Build reconcile failed for {Id}", prototypeId);
                    // leave as building; next read retries
                }
            }
        }
        return Ok(proto);
    }

    /// <summary>Build + deploy a functional prototype's repo (kicks off; async).</summary>
    [HttpPost("{id}/build")]
    public async Task<IActionResult> Build(string id, CancellationToken ct)
    {
        if (!supabase.Value.IsConfigured)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "not_configured" });
        if (!Guid.TryParse(id, out var prototypeId)) return NotFound();

        var me = await ResolveContextAsync(ct);
        if (me is null) return NotFound(new { error = "not_provisioned" });
        var tenantId = Guid.Parse(me.Tenant.Id);

        var repo = HttpContext.RequestServices.GetRequiredService<PrototypeRepository>();
        var proto = await repo.GetAsync(tenantId, prototypeId, ct);
        if (proto is null) return NotFound();
        if (proto.Type != "functional")
            return BadRequest(new { error = "not_functional", detail = "only functional prototypes can be built" });
        if (string.IsNullOrWhiteSpace(proto.GithubRepoUrl))
            return BadRequest(new { error = "no_repo" });

        var runner = HttpContext.RequestServices.GetRequiredService<GcpRunnerService>();
        if (!runner.IsConfigured)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "runtime_not_configured" });

        // Skip-if-unchanged: if it's already running the current commit, reuse it.
        var sha = await ResolveShaAsync(HttpContext.RequestServices, proto.GithubRepoUrl, proto.GithubBranch, ct);
        if (sha is not null && proto.BuildStatus == "ready" && proto.RunUrl is not null)
        {
            var built = await repo.GetRunCommitAsync(tenantId, prototypeId, ct);
            if (string.Equals(built, sha, StringComparison.Ordinal))
                return Ok(new { status = "ready", runUrl = proto.RunUrl, skipped = true });
        }

        try
        {
            await StartAndRecordAsync(HttpContext.RequestServices, tenantId, prototypeId, proto.GithubRepoUrl, proto.GithubBranch, sha, ct);
        }
        catch (Exception e)
        {
            await repo.SetBuildResultAsync(tenantId, prototypeId, "failed", null, e.Message, ct);
            return BadRequest(new { error = "build_start_failed", detail = e.Message });
        }

        logger.LogInformation("Started build for prototype {Id}", prototypeId);
        return Accepted(new { status = "building" });
    }

    /// <summary>The Cloud Build log for a prototype's latest build (for diagnosing failures).</summary>
    [HttpGet("{id}/build-log")]
    public async Task<IActionResult> BuildLog(string id, CancellationToken ct)
    {
        if (!supabase.Value.IsConfigured)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "not_configured" });
        if (!Guid.TryParse(id, out var prototypeId)) return NotFound();

        var me = await ResolveContextAsync(ct);
        if (me is null) return NotFound(new { error = "not_provisioned" });

        var repo = HttpContext.RequestServices.GetRequiredService<PrototypeRepository>();
        var buildId = await repo.GetBuildIdAsync(Guid.Parse(me.Tenant.Id), prototypeId, ct);
        if (buildId is null) return Ok(new { log = "" });

        var runner = HttpContext.RequestServices.GetRequiredService<GcpRunnerService>();
        if (!runner.IsConfigured) return Ok(new { log = "" });
        try
        {
            return Ok(new { log = await runner.GetBuildLogAsync(buildId, ct) });
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Build-log fetch failed for {Id}", prototypeId);
            return Ok(new { log = "", error = e.Message });
        }
    }

    /// <summary>Stop a running prototype (delete the Cloud Run service).</summary>
    [HttpPost("{id}/teardown")]
    public async Task<IActionResult> Teardown(string id, CancellationToken ct)
    {
        if (!supabase.Value.IsConfigured)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "not_configured" });
        if (!Guid.TryParse(id, out var prototypeId)) return NotFound();

        var me = await ResolveContextAsync(ct);
        if (me is null) return NotFound(new { error = "not_provisioned" });
        var tenantId = Guid.Parse(me.Tenant.Id);

        var repo = HttpContext.RequestServices.GetRequiredService<PrototypeRepository>();
        var proto = await repo.GetAsync(tenantId, prototypeId, ct);
        if (proto is null) return NotFound();

        var runner = HttpContext.RequestServices.GetRequiredService<GcpRunnerService>();
        if (runner.IsConfigured)
        {
            try { await runner.DeleteServiceAsync(prototypeId.ToString(), ct); }
            catch (Exception e) { logger.LogWarning(e, "Teardown of {Id} failed", prototypeId); }
        }
        await repo.ClearRunAsync(tenantId, prototypeId, ct);
        return NoContent();
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
