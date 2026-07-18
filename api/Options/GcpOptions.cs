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

    // Identifiers are trimmed on set — env vars (esp. pasted into hosting UIs)
    // pick up stray whitespace, and a leading space in the region silently
    // corrupts every image/URL the pipeline builds.
    private string _projectId = string.Empty;
    public string ProjectId { get => _projectId; set => _projectId = value?.Trim() ?? string.Empty; }

    private string _region = "us-central1";
    public string Region { get => _region; set => _region = string.IsNullOrWhiteSpace(value) ? "us-central1" : value.Trim(); }

    private string _artifactRepo = "proto-prototypes";
    public string ArtifactRepo { get => _artifactRepo; set => _artifactRepo = string.IsNullOrWhiteSpace(value) ? "proto-prototypes" : value.Trim(); }

    private string _stagingBucket = string.Empty;
    /// <summary>GCS bucket for build source uploads; defaults to &lt;project&gt;-proto-src.</summary>
    public string StagingBucket { get => _stagingBucket; set => _stagingBucket = value?.Trim() ?? string.Empty; }

    /// <summary>Service-account key JSON (preferred on Railway).</summary>
    public string CredentialsJson { get; set; } = string.Empty;

    private string _credentialsFile = string.Empty;
    /// <summary>Path to the service-account key file (convenient in local dev).</summary>
    public string CredentialsFile { get => _credentialsFile; set => _credentialsFile = value?.Trim() ?? string.Empty; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ProjectId)
        && (!string.IsNullOrWhiteSpace(CredentialsJson) || !string.IsNullOrWhiteSpace(CredentialsFile));

    public string EffectiveStagingBucket =>
        string.IsNullOrWhiteSpace(StagingBucket) ? $"{ProjectId}-proto-src" : StagingBucket;
}
