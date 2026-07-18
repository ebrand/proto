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
                   github_repo_url, github_branch, created_at, updated_at
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
            r.GetDateTime(9).ToUniversalTime().ToString("o"));
    }
}
