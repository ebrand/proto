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
            select id, name, kind::text, order_index, is_entry_page, route, image_url,
                   canvas_x, canvas_y, created_at
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
                r.IsDBNull(7) ? null : r.GetDouble(7),
                r.IsDBNull(8) ? null : r.GetDouble(8),
                r.GetDateTime(9).ToUniversalTime().ToString("o")));
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

    /// <summary>Rename a page. Scoped by tenant + prototype. False if absent.</summary>
    public async Task<bool> RenameAsync(
        Guid tenantId, Guid prototypeId, Guid pageId, string name, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            update public.ux_pages set name = @name, updated_at = now()
            where id = @id and tenant_id = @tid and prototype_id = @pid
            """, conn);
        cmd.Parameters.AddWithValue("id", pageId);
        cmd.Parameters.AddWithValue("tid", tenantId);
        cmd.Parameters.AddWithValue("pid", prototypeId);
        cmd.Parameters.AddWithValue("name", name);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    /// <summary>
    /// Delete a page (its hotspots cascade; incoming links null out at the DB).
    /// Returns whether a row was deleted and the old image path (if any) so the
    /// caller can clean up the storage object. The `old` CTE reads the path
    /// before `del` removes the row.
    /// </summary>
    public async Task<(bool Deleted, string? ImagePath)> DeleteAsync(
        Guid tenantId, Guid prototypeId, Guid pageId, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            with old as (
                select image_media_id as path from public.ux_pages
                where id = @id and tenant_id = @tid and prototype_id = @pid
            ),
            del as (
                delete from public.ux_pages
                where id = @id and tenant_id = @tid and prototype_id = @pid
                returning 1
            )
            select (select count(*) from del)::int as deleted, (select path from old) as path
            """, conn);
        cmd.Parameters.AddWithValue("id", pageId);
        cmd.Parameters.AddWithValue("tid", tenantId);
        cmd.Parameters.AddWithValue("pid", prototypeId);

        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return (false, null);
        var deleted = r.GetInt32(0) > 0;
        var path = r.IsDBNull(1) ? null : r.GetString(1);
        return (deleted, path);
    }

    /// <summary>True if the page exists in the given tenant + prototype.</summary>
    public async Task<bool> PageBelongsAsync(
        Guid tenantId, Guid prototypeId, Guid pageId, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            select 1 from public.ux_pages
            where id = @id and tenant_id = @tid and prototype_id = @pid
            """, conn);
        cmd.Parameters.AddWithValue("id", pageId);
        cmd.Parameters.AddWithValue("tid", tenantId);
        cmd.Parameters.AddWithValue("pid", prototypeId);
        return await cmd.ExecuteScalarAsync(ct) is not null;
    }

    /// <summary>
    /// Record an uploaded image on a page (all four image columns, per the
    /// all-or-none DB constraint). Scoped by tenant + prototype. False if absent.
    /// </summary>
    public async Task<bool> SetImageAsync(
        Guid tenantId, Guid prototypeId, Guid pageId,
        string mediaId, string url, int width, int height, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            update public.ux_pages
            set image_media_id = @mid, image_url = @url,
                image_width = @w, image_height = @h, updated_at = now()
            where id = @id and tenant_id = @tid and prototype_id = @pid
            """, conn);
        cmd.Parameters.AddWithValue("id", pageId);
        cmd.Parameters.AddWithValue("tid", tenantId);
        cmd.Parameters.AddWithValue("pid", prototypeId);
        cmd.Parameters.AddWithValue("mid", mediaId);
        cmd.Parameters.AddWithValue("url", url);
        cmd.Parameters.AddWithValue("w", width);
        cmd.Parameters.AddWithValue("h", height);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    /// <summary>
    /// Clear a page's image columns and return the old media id (storage path)
    /// so the caller can delete the object. Null if the page had no image or
    /// isn't in scope.
    /// </summary>
    public async Task<string?> ClearImageAsync(
        Guid tenantId, Guid prototypeId, Guid pageId, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        // The `old` CTE reads the current path before `cleared` nulls it — a
        // plain UPDATE ... RETURNING would hand back the new (null) value.
        await using var cmd = new NpgsqlCommand(
            """
            with old as (
                select image_media_id as path
                from public.ux_pages
                where id = @id and tenant_id = @tid and prototype_id = @pid
                  and image_media_id is not null
            ),
            cleared as (
                update public.ux_pages
                set image_media_id = null, image_url = null,
                    image_width = null, image_height = null, updated_at = now()
                where id = @id and tenant_id = @tid and prototype_id = @pid
                  and image_media_id is not null
                returning 1
            )
            select path from old
            """, conn);
        cmd.Parameters.AddWithValue("id", pageId);
        cmd.Parameters.AddWithValue("tid", tenantId);
        cmd.Parameters.AddWithValue("pid", prototypeId);
        return await cmd.ExecuteScalarAsync(ct) as string;
    }

    /// <summary>
    /// Persist a page's canvas position. Scoped by tenant + prototype so a caller
    /// cannot move a page in another tenant's (or another prototype's) canvas.
    /// Returns false if no such page exists in that scope.
    /// </summary>
    public async Task<bool> UpdatePositionAsync(
        Guid tenantId, Guid prototypeId, Guid pageId, double x, double y, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            update public.ux_pages
            set canvas_x = @x, canvas_y = @y, updated_at = now()
            where id = @id and tenant_id = @tid and prototype_id = @pid
            """, conn);
        cmd.Parameters.AddWithValue("id", pageId);
        cmd.Parameters.AddWithValue("tid", tenantId);
        cmd.Parameters.AddWithValue("pid", prototypeId);
        cmd.Parameters.AddWithValue("x", x);
        cmd.Parameters.AddWithValue("y", y);

        var affected = await cmd.ExecuteNonQueryAsync(ct);
        return affected > 0;
    }
}
