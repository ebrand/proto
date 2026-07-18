using System.Net;
using System.Text.Json;
using Proto.Api.Models;

namespace Proto.Api.Services;

/// <summary>
/// Inspects a public GitHub repo for the "define a prototype" workflow: parses
/// the URL, asks the GitHub API for the repo, and reports the primary language
/// + whether it's in Proto's supported set. Unauthenticated (public repos only,
/// GitHub rate-limits to 60/hr per IP) — private-repo support is a later step.
/// </summary>
public sealed class GitHubClient(HttpClient http)
{
    // v1 supported languages (extensible). Compared case-insensitively.
    public static readonly string[] Supported = ["go", "python", "typescript", "javascript"];

    public async Task<RepoInspection> InspectAsync(string repoUrl, CancellationToken ct)
    {
        if (!TryParseRepo(repoUrl, out var owner, out var repo))
        {
            return new RepoInspection(false, null, null, null, null, false, false,
                "Enter a github.com/owner/repo URL.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{owner}/{repo}");
        request.Headers.UserAgent.ParseAdd("proto-app");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new RepoInspection(false, owner, repo, null, null, false, false,
                "Repository not found (or it's private — public repos only for now).");
        }
        if (!response.IsSuccessStatusCode)
        {
            return new RepoInspection(false, owner, repo, null, null, false, false,
                $"GitHub returned {(int)response.StatusCode}.");
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        var name = root.TryGetProperty("name", out var n) ? n.GetString() : repo;
        var branch = root.TryGetProperty("default_branch", out var b) ? b.GetString() : null;
        var language = root.TryGetProperty("language", out var l) && l.ValueKind == JsonValueKind.String
            ? l.GetString()
            : null;
        var isPrivate = root.TryGetProperty("private", out var p) && p.GetBoolean();
        var supported = language is not null && Supported.Contains(language.ToLowerInvariant());

        var message = supported
            ? null
            : language is null
                ? "Couldn't detect a language for this repo."
                : $"{language} isn't supported yet (Go, Python, TypeScript, JavaScript).";

        return new RepoInspection(true, owner, name, branch, language, supported, isPrivate, message);
    }

    /// <summary>
    /// Download the repo as a gzipped tarball (public repos). GitHub's archive
    /// wraps everything in a single top-level dir (owner-repo-sha/) — callers
    /// that feed this to a builder must strip it.
    /// </summary>
    public async Task<byte[]> DownloadTarballAsync(string owner, string repo, string? gitRef, CancellationToken ct)
    {
        var path = string.IsNullOrWhiteSpace(gitRef) || gitRef == "HEAD"
            ? $"https://api.github.com/repos/{owner}/{repo}/tarball"
            : $"https://api.github.com/repos/{owner}/{repo}/tarball/{Uri.EscapeDataString(gitRef)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.UserAgent.ParseAdd("proto-app");
        using var response = await http.SendAsync(request, ct); // HttpClient follows the redirect to codeload
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    /// <summary>Public wrapper over the URL parser.</summary>
    public static bool TryParse(string url, out string owner, out string repo) =>
        TryParseRepo(url, out owner, out repo);

    /// <summary>Parse github URLs (https + git@) into owner/repo.</summary>
    private static bool TryParseRepo(string url, out string owner, out string repo)
    {
        owner = repo = string.Empty;
        if (string.IsNullOrWhiteSpace(url)) return false;

        var s = url.Trim().Replace("git@github.com:", "https://github.com/");
        if (!Uri.TryCreate(s, UriKind.Absolute, out var uri) || !uri.Host.Contains("github.com"))
        {
            return false;
        }

        var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;

        owner = parts[0];
        repo = parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? parts[1][..^4] : parts[1];
        return owner.Length > 0 && repo.Length > 0;
    }
}
