namespace Proto.Api.Options;

/// <summary>
/// Supabase Postgres connection settings. Bound from the "Supabase" section.
/// The .NET API is the sole DB writer and connects with a privileged role
/// (service role / a dedicated Postgres role), which bypasses RLS by design —
/// tenant scoping is enforced in application code, not by RLS. The connection
/// string is a SECRET; supply it via user-secrets / environment variables.
/// </summary>
public sealed class SupabaseOptions
{
    public const string SectionName = "Supabase";

    /// <summary>Npgsql connection string to the project's Postgres instance.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ConnectionString);
}
