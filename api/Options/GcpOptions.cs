namespace Proto.Api.Options;

/// <summary>
/// Google Cloud config for the functional-prototype runtime: build a repo
/// (Cloud Build + buildpacks) and run it on Cloud Run. The API authenticates as
/// a service account (proto-api-runner) whose key is supplied as JSON (env var,
/// preferred for deploy) or a file path (convenient locally). SECRET — never
/// commit the key.
/// </summary>
public sealed class GcpOptions
{
    public const string SectionName = "Gcp";

    public string ProjectId { get; set; } = string.Empty;
    public string Region { get; set; } = "us-central1";
    public string ArtifactRepo { get; set; } = "proto-prototypes";

    /// <summary>GCS bucket for build source uploads; defaults to &lt;project&gt;-proto-src.</summary>
    public string StagingBucket { get; set; } = string.Empty;

    /// <summary>Service-account key JSON (preferred on Railway).</summary>
    public string CredentialsJson { get; set; } = string.Empty;

    /// <summary>Path to the service-account key file (convenient in local dev).</summary>
    public string CredentialsFile { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ProjectId)
        && (!string.IsNullOrWhiteSpace(CredentialsJson) || !string.IsNullOrWhiteSpace(CredentialsFile));

    public string EffectiveStagingBucket =>
        string.IsNullOrWhiteSpace(StagingBucket) ? $"{ProjectId}-proto-src" : StagingBucket;
}
