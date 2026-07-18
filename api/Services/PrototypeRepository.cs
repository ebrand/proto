using Npgsql;
using Proto.Api.Models;

namespace Proto.Api.Services;

/// <summary>
/// Data access for prototypes. Every query is filtered by tenant_id — a caller
/// can only ever see or create prototypes within their own tenant.
/// </summary>
public sealed class PrototypeRepository(NpgsqlDataSource dataSource)
{
    public async Task<IReadOnlyList<PrototypeSummary>> ListAsync(Guid tenantId, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            select id, name, description, type::text, status::text, created_at
            from public.prototypes
            where tenant_id = @tid
            order by created_at desc
            """, conn);
        cmd.Parameters.AddWithValue("tid", tenantId);

        var result = new List<PrototypeSummary>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            result.Add(new PrototypeSummary(
                r.GetGuid(0).ToString(),
                r.GetString(1),
                r.IsDBNull(2) ? null : r.GetString(2),
                r.GetString(3),
                r.GetString(4),
                r.GetDateTime(5).ToUniversalTime().ToString("o")));
        }
        return result;
    }

    public async Task<string> CreateAsync(
        Guid tenantId, Guid ownerId, string name, string type,
        string? description, string? githubRepoUrl, string? githubBranch, string? language, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            insert into public.prototypes
              (tenant_id, name, description, type, owner_id, github_repo_url, github_branch, language)
            values
              (@tid, @name, @desc, @type::prototype_type, @owner, @repo, @branch, @lang)
            returning id
            """, conn);
        cmd.Parameters.AddWithValue("tid", tenantId);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("desc", (object?)description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("type", type);
        cmd.Parameters.AddWithValue("owner", ownerId);
        cmd.Parameters.AddWithValue("repo", (object?)githubRepoUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("branch", (object?)githubBranch ?? DBNull.Value);
        cmd.Parameters.AddWithValue("lang", (object?)language ?? DBNull.Value);

        var id = (Guid)(await cmd.ExecuteScalarAsync(ct))!;
        return id.ToString();
    }

    public async Task<PrototypeDetail?> GetAsync(Guid tenantId, Guid prototypeId, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            select id, name, description, type::text, status::text, owner_id,
                   github_repo_url, github_branch, created_at, updated_at,
                   build_status, run_url, build_error
            from public.prototypes
            where tenant_id = @tid and id = @id
            """, conn);
        cmd.Parameters.AddWithValue("tid", tenantId);
        cmd.Parameters.AddWithValue("id", prototypeId);

        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new PrototypeDetail(
            r.GetGuid(0).ToString(),
            r.GetString(1),
            r.IsDBNull(2) ? null : r.GetString(2),
            r.GetString(3),
            r.GetString(4),
            r.GetGuid(5).ToString(),
            r.IsDBNull(6) ? null : r.GetString(6),
            r.IsDBNull(7) ? null : r.GetString(7),
            r.GetDateTime(8).ToUniversalTime().ToString("o"),
            r.GetDateTime(9).ToUniversalTime().ToString("o"),
            r.IsDBNull(10) ? null : r.GetString(10),
            r.IsDBNull(11) ? null : r.GetString(11),
            r.IsDBNull(12) ? null : r.GetString(12));
    }

    /// <summary>The in-flight Cloud Build id for a prototype (for reconcile).</summary>
    public async Task<string?> GetBuildIdAsync(Guid tenantId, Guid prototypeId, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "select build_id from public.prototypes where tenant_id = @tid and id = @id", conn);
        cmd.Parameters.AddWithValue("tid", tenantId);
        cmd.Parameters.AddWithValue("id", prototypeId);
        return await cmd.ExecuteScalarAsync(ct) as string;
    }

    /// <summary>The commit SHA the running service was built from (for skip-if-unchanged).</summary>
    public async Task<string?> GetRunCommitAsync(Guid tenantId, Guid prototypeId, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "select run_commit from public.prototypes where tenant_id = @tid and id = @id", conn);
        cmd.Parameters.AddWithValue("tid", tenantId);
        cmd.Parameters.AddWithValue("id", prototypeId);
        return await cmd.ExecuteScalarAsync(ct) as string;
    }

    /// <summary>Mark a build as started (status=building) with its id, service, and the commit being built.</summary>
    public async Task SetBuildStartedAsync(
        Guid tenantId, Guid prototypeId, string buildId, string runService, string? commit, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            update public.prototypes
            set build_status = 'building', build_id = @bid, run_service = @svc, run_commit = @commit,
                build_error = null, last_built_at = now(), updated_at = now()
            where tenant_id = @tid and id = @id
            """, conn);
        cmd.Parameters.AddWithValue("tid", tenantId);
        cmd.Parameters.AddWithValue("id", prototypeId);
        cmd.Parameters.AddWithValue("bid", buildId);
        cmd.Parameters.AddWithValue("svc", runService);
        cmd.Parameters.AddWithValue("commit", (object?)commit ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Record a terminal build outcome (ready + url, or failed + error).</summary>
    public async Task SetBuildResultAsync(
        Guid tenantId, Guid prototypeId, string status, string? runUrl, string? error, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            update public.prototypes
            set build_status = @st, run_url = @url, build_error = @err, updated_at = now()
            where tenant_id = @tid and id = @id
            """, conn);
        cmd.Parameters.AddWithValue("tid", tenantId);
        cmd.Parameters.AddWithValue("id", prototypeId);
        cmd.Parameters.AddWithValue("st", status);
        cmd.Parameters.AddWithValue("url", (object?)runUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("err", (object?)error ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Clear all build/run state (after teardown).</summary>
    public async Task ClearRunAsync(Guid tenantId, Guid prototypeId, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            update public.prototypes
            set build_status = null, build_id = null, run_url = null,
                run_service = null, build_error = null, updated_at = now()
            where tenant_id = @tid and id = @id
            """, conn);
        cmd.Parameters.AddWithValue("tid", tenantId);
        cmd.Parameters.AddWithValue("id", prototypeId);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
