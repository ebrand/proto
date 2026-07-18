using Npgsql;
using Proto.Api.Models;

namespace Proto.Api.Services;

/// <summary>
/// Data access for hotspots (flow-map links). Every query is scoped by
/// tenant_id and, via a join to ux_pages, by prototype_id — a caller can only
/// touch hotspots on pages in their own tenant's prototype.
/// </summary>
public sealed class HotspotRepository(NpgsqlDataSource dataSource)
{
    /// <summary>All hotspots across the pages of one prototype.</summary>
    public async Task<IReadOnlyList<HotspotSummary>> ListByPrototypeAsync(
        Guid tenantId, Guid prototypeId, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            select h.id, h.ux_page_id, h.shape::text, h.label,
                   h.rect_x, h.rect_y, h.rect_width, h.rect_height,
                   h.target_page_id, h.target_external_url, h.created_at
            from public.hotspots h
            join public.ux_pages p on p.id = h.ux_page_id
            where h.tenant_id = @tid and p.prototype_id = @pid
            order by h.created_at
            """, conn);
        cmd.Parameters.AddWithValue("tid", tenantId);
        cmd.Parameters.AddWithValue("pid", prototypeId);

        var result = new List<HotspotSummary>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            result.Add(new HotspotSummary(
                r.GetGuid(0).ToString(),
                r.GetGuid(1).ToString(),
                r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.IsDBNull(4) ? null : r.GetDouble(4),
                r.IsDBNull(5) ? null : r.GetDouble(5),
                r.IsDBNull(6) ? null : r.GetDouble(6),
                r.IsDBNull(7) ? null : r.GetDouble(7),
                r.IsDBNull(8) ? null : r.GetGuid(8).ToString(),
                r.IsDBNull(9) ? null : r.GetString(9),
                r.GetDateTime(10).ToUniversalTime().ToString("o")));
        }
        return result;
    }

    /// <summary>
    /// Create a rect hotspot on a page. The insert is guarded: it only writes
    /// when the source page (and the target page, when given) belong to the
    /// same tenant + prototype. Returns the new id, or null when that guard
    /// fails (page not in scope) so the controller can 404.
    /// </summary>
    public async Task<string?> CreateRectAsync(
        Guid tenantId, Guid prototypeId, Guid uxPageId, string? label,
        double rectX, double rectY, double rectWidth, double rectHeight,
        Guid? targetPageId, string? targetExternalUrl, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            insert into public.hotspots
              (ux_page_id, tenant_id, shape, label, rect_x, rect_y, rect_width, rect_height,
               target_page_id, target_external_url)
            select @pageId, @tid, 'rect', @label, @rx, @ry, @rw, @rh, @target, @url
            where exists (
                select 1 from public.ux_pages
                where id = @pageId and tenant_id = @tid and prototype_id = @pid)
              and (@target is null or exists (
                select 1 from public.ux_pages
                where id = @target and tenant_id = @tid and prototype_id = @pid))
            returning id
            """, conn);
        cmd.Parameters.AddWithValue("pageId", uxPageId);
        cmd.Parameters.AddWithValue("tid", tenantId);
        cmd.Parameters.AddWithValue("pid", prototypeId);
        cmd.Parameters.AddWithValue("label", (object?)label ?? DBNull.Value);
        cmd.Parameters.AddWithValue("rx", rectX);
        cmd.Parameters.AddWithValue("ry", rectY);
        cmd.Parameters.AddWithValue("rw", rectWidth);
        cmd.Parameters.AddWithValue("rh", rectHeight);
        cmd.Parameters.AddWithValue("target", (object?)targetPageId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("url", (object?)targetExternalUrl ?? DBNull.Value);

        var id = await cmd.ExecuteScalarAsync(ct);
        return id is Guid g ? g.ToString() : null;
    }

    /// <summary>Delete a hotspot, scoped by tenant + prototype. False if absent.</summary>
    public async Task<bool> DeleteAsync(
        Guid tenantId, Guid prototypeId, Guid hotspotId, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            delete from public.hotspots h
            using public.ux_pages p
            where h.id = @id and h.ux_page_id = p.id
              and h.tenant_id = @tid and p.prototype_id = @pid
            """, conn);
        cmd.Parameters.AddWithValue("id", hotspotId);
        cmd.Parameters.AddWithValue("tid", tenantId);
        cmd.Parameters.AddWithValue("pid", prototypeId);

        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }
}
