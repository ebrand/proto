namespace Proto.Api.Models;

// Contracts for the Prototypes API. A prototype is "functional" (backed by a
// GitHub repo) or "illustrative" (static mockups). All are tenant-scoped.

public sealed record CreatePrototypeRequest(
    string Name,
    string Type,              // "functional" | "illustrative"
    string? Description,
    string? GithubRepoUrl,    // required when Type = functional
    string? GithubBranch,
    string? Language);        // detected during the define-a-prototype workflow

public sealed record RepoInspectRequest(string RepoUrl);

/// <summary>Result of inspecting a GitHub repo for the define-a-prototype workflow.</summary>
public sealed record RepoInspection(
    bool Accessible,
    string? Owner,
    string? RepoName,
    string? DefaultBranch,
    string? DetectedLanguage,
    bool Supported,
    bool IsPrivate,
    string? Message);

public sealed record PrototypeSummary(
    string Id,
    string Name,
    string? Description,
    string Type,
    string Status,
    string CreatedAt);

public sealed record PrototypeDetail(
    string Id,
    string Name,
    string? Description,
    string Type,
    string Status,
    string OwnerId,
    string? GithubRepoUrl,
    string? GithubBranch,
    string CreatedAt,
    string UpdatedAt);
