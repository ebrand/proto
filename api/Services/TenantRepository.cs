using Npgsql;
using Proto.Api.Models;

namespace Proto.Api.Services;

/// <summary>
/// Postgres data access for tenant provisioning, via Npgsql against the
/// Supabase session pooler. Connects with a privileged role (bypasses RLS by
/// design); tenant scoping is enforced in application code. All logic lives
/// here in C# — no PL/pgSQL functions.
/// </summary>
public sealed class TenantRepository(NpgsqlDataSource dataSource)
{
    /// <summary>
    /// Insert the tenant + its admin user in a single transaction, after
    /// validating the tier. Returns the new ids. Throws
    /// <see cref="ProvisioningException"/> for an unknown tier or a uniqueness
    /// conflict (slug / already-provisioned org).
    /// </summary>
    public async Task<(string TenantId, string UserId)> ProvisionTenantAsync(
        string tierCode,
        string orgName,
        string orgSlug,
        string stytchOrgId,
        string memberEmail,
        string memberName,
        string stytchMemberId,
        CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            var tierId = await ScalarOrNullAsync<Guid>(conn, tx, ct,
                "select id from public.subscription_tiers where code = @code::subscription_tier_code and is_active",
                ("code", tierCode));
            if (tierId is null)
            {
                throw new ProvisioningException($"unknown or inactive subscription tier: {tierCode}");
            }

            var tenantId = await ScalarAsync<Guid>(conn, tx, ct,
                """
                insert into public.tenants
                  (name, slug, subscription_tier_id, subscription_tier_code, stytch_organization_id)
                values
                  (@name, @slug, @tierId, @code::subscription_tier_code, @orgId)
                returning id
                """,
                ("name", orgName), ("slug", orgSlug), ("tierId", tierId.Value),
                ("code", tierCode), ("orgId", stytchOrgId));

            var userId = await ScalarAsync<Guid>(conn, tx, ct,
                """
                insert into public.users
                  (tenant_id, email, display_name, status, tenant_role, stytch_member_id)
                values
                  (@tenantId, @email, @name, 'active'::user_status, 'admin'::tenant_role, @memberId)
                returning id
                """,
                ("tenantId", tenantId), ("email", memberEmail),
                ("name", memberName), ("memberId", stytchMemberId));

            await tx.CommitAsync(ct);
            return (tenantId.ToString(), userId.ToString());
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // tx is rolled back on dispose. A conflict here means the org was
            // already provisioned or the slug is taken.
            throw new ProvisioningException(
                $"tenant already provisioned or slug taken (constraint: {ex.ConstraintName})", ex);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.InvalidTextRepresentation)
        {
            // e.g. a tierCode that isn't a valid subscription_tier_code enum value.
            throw new ProvisioningException($"unknown subscription tier: {tierCode}", ex);
        }
    }

    /// <summary>
    /// Resolve a Stytch member id to the Proto user + tenant it maps to (via the
    /// stytch_member_id sync key). Returns null when the session is valid but no
    /// Proto rows exist yet (e.g. an org that was never provisioned).
    /// </summary>
    public async Task<MeResponse?> FindMemberContextAsync(string stytchMemberId, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            select u.id, u.email, u.display_name, u.status::text, u.tenant_role::text,
                   t.id, t.name, t.slug, t.subscription_tier_code::text, t.subscription_status::text
            from public.users u
            join public.tenants t on t.id = u.tenant_id
            where u.stytch_member_id = @mid
            """, conn);
        cmd.Parameters.AddWithValue("mid", stytchMemberId);

        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;

        return new MeResponse(
            new MeUser(r.GetGuid(0).ToString(), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4)),
            new MeTenant(r.GetGuid(5).ToString(), r.GetString(6), r.GetString(7), r.GetString(8), r.GetString(9)));
    }

    private static async Task<T> ScalarAsync<T>(
        NpgsqlConnection conn, NpgsqlTransaction tx, CancellationToken ct,
        string sql, params (string Name, object Value)[] parameters)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value);
        var result = await cmd.ExecuteScalarAsync(ct);
        return (T)result!;
    }

    private static async Task<T?> ScalarOrNullAsync<T>(
        NpgsqlConnection conn, NpgsqlTransaction tx, CancellationToken ct,
        string sql, params (string Name, object Value)[] parameters) where T : struct
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is null or DBNull ? null : (T)result;
    }
}

/// <summary>Raised when tenant provisioning fails at the data layer.</summary>
public sealed class ProvisioningException(string message, Exception? inner = null)
    : Exception(message, inner);
