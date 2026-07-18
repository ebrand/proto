using Npgsql;
using Proto.Api.Models;

namespace Proto.Api.Services;

/// <summary>
/// Data access for UX pages. Scoped by both tenant_id and prototype_id — the
/// controller first confirms the prototype belongs to the caller's tenant, and
/// every query here re-filters by tenant_id as defense-in-depth.
/// </summary>
public sealed class UxPageRepository(NpgsqlDataSource dataSource)
{
    public async Task<IReadOnlyList<UxPageSummary>> ListAsync(
        Guid tenantId, Guid prototypeId, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            select id, name, kind::text, order_index, is_entry_page, route, image_url, created_at
            from public.ux_pages
            where tenant_id = @tid and prototype_id = @pid
            order by order_index, created_at
            """, conn);
        cmd.Parameters.AddWithValue("tid", tenantId);
        cmd.Parameters.AddWithValue("pid", prototypeId);

        var result = new List<UxPageSummary>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            result.Add(new UxPageSummary(
                r.GetGuid(0).ToString(),
                r.GetString(1),
                r.GetString(2),
                r.GetInt32(3),
                r.GetBoolean(4),
                r.IsDBNull(5) ? null : r.GetString(5),
                r.IsDBNull(6) ? null : r.GetString(6),
                r.GetDateTime(7).ToUniversalTime().ToString("o")));
        }
        return result;
    }

    public async Task<string> CreateAsync(
        Guid tenantId, Guid prototypeId, string name, string kind,
        int orderIndex, bool isEntryPage, string? route, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            insert into public.ux_pages
              (prototype_id, tenant_id, name, order_index, is_entry_page, kind, route)
            values
              (@pid, @tid, @name, @order, @entry, @kind::ux_page_kind, @route)
            returning id
            """, conn);
        cmd.Parameters.AddWithValue("pid", prototypeId);
        cmd.Parameters.AddWithValue("tid", tenantId);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("order", orderIndex);
        cmd.Parameters.AddWithValue("entry", isEntryPage);
        cmd.Parameters.AddWithValue("kind", kind);
        cmd.Parameters.AddWithValue("route", (object?)route ?? DBNull.Value);

        var id = (Guid)(await cmd.ExecuteScalarAsync(ct))!;
        return id.ToString();
    }
}
