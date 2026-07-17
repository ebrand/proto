namespace Proto.Api.Options;

/// <summary>
/// Supabase Postgres access. The .NET API is the sole DB caller and connects
/// with a privileged role via Npgsql, which bypasses RLS by design — tenant
/// scoping is enforced in application code, not by RLS. All data logic lives in
/// C# (no PL/pgSQL functions).
///
/// Use the Supabase <strong>Session pooler</strong> connection string
/// (host <c>aws-0-&lt;region&gt;.pooler.supabase.com</c>, user
/// <c>postgres.&lt;ref&gt;</c>): the direct <c>db.&lt;ref&gt;.supabase.co</c>
/// host is IPv6-only and won't resolve on IPv4 networks. The string is a SECRET;
/// supply it via user-secrets / env vars.
/// </summary>
public sealed class SupabaseOptions
{
    public const string SectionName = "Supabase";

    /// <summary>Npgsql (name=value) connection string to Supabase Postgres.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ConnectionString);
}
