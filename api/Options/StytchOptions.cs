namespace Proto.Api.Options;

/// <summary>
/// Stytch B2B backend credentials. Bound from the "Stytch" configuration
/// section. The secret is NEVER committed — supply it via user-secrets in
/// development or environment variables in deployment (see api/README.md).
/// </summary>
public sealed class StytchOptions
{
    public const string SectionName = "Stytch";

    /// <summary>Stytch project id, e.g. project-test-xxxx. Maps to ClientConfig.ProjectId.</summary>
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>Stytch project secret. Maps to ClientConfig.ProjectSecret. Secret — do not commit.</summary>
    public string ProjectSecret { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ProjectId) && !string.IsNullOrWhiteSpace(ProjectSecret);
}
