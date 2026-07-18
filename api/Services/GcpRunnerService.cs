using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Options;
using Proto.Api.Options;

namespace Proto.Api.Services;

/// <summary>Outcome of a build+deploy run.</summary>
public sealed record BuildDeployResult(bool Success, string? Url, string ServiceName, string? BuildId, string? Error);

/// <summary>
/// Functional-prototype runtime on Google Cloud: fetch the repo → strip GitHub's
/// top-level dir → upload source to GCS → one Cloud Build (buildpacks build +
/// publish + `run deploy`, run as the proto-api-runner SA) → return the Cloud
/// Run URL. Authenticates as the service account; talks to the GCP REST APIs
/// with a bearer token (matching the codebase's raw-HttpClient style).
///
/// The exact recipe was proven via gcloud before being ported here.
/// </summary>
public sealed class GcpRunnerService(HttpClient http, GitHubClient github, IOptions<GcpOptions> options)
{
    private static readonly string[] Scopes = ["https://www.googleapis.com/auth/cloud-platform"];
    private static readonly HashSet<string> TerminalStatuses =
        ["SUCCESS", "FAILURE", "INTERNAL_ERROR", "TIMEOUT", "CANCELLED", "EXPIRED"];

    private GcpOptions Opt => options.Value;
    public bool IsConfigured => Opt.IsConfigured;

    // --- auth ---------------------------------------------------------------

    private GoogleCredential BuildCredential()
    {
        var cred = !string.IsNullOrWhiteSpace(Opt.CredentialsJson)
            ? GoogleCredential.FromJson(Opt.CredentialsJson)
            : GoogleCredential.FromFile(Opt.CredentialsFile);
        return cred.CreateScoped(Scopes);
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken ct) =>
        await BuildCredential().UnderlyingCredential.GetAccessTokenForRequestAsync(cancellationToken: ct);

    private string ServiceAccountEmail()
    {
        var json = !string.IsNullOrWhiteSpace(Opt.CredentialsJson)
            ? Opt.CredentialsJson
            : File.ReadAllText(Opt.CredentialsFile);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("client_email").GetString()!;
    }

    /// <summary>Cloud Run service name for a prototype (lowercase, &lt;=63 chars).</summary>
    public static string ServiceNameFor(string prototypeId) => $"proto-{prototypeId}";

    // --- pipeline -----------------------------------------------------------

    public enum BuildState { Building, Ready, Failed }

    /// <summary>
    /// Kick off a build+deploy: fetch source, upload to GCS, create the Cloud
    /// Build (which builds and deploys). Returns fast — the long work runs in
    /// Cloud Build. Poll <see cref="CheckBuildAsync"/> for progress.
    /// </summary>
    public async Task<string> StartBuildAsync(
        string prototypeId, string repoUrl, string? gitRef, CancellationToken ct)
    {
        if (!GitHubClient.TryParse(repoUrl, out var owner, out var repo))
            throw new InvalidOperationException("Not a valid GitHub repo URL.");

        var tgz = await github.DownloadTarballAsync(owner, repo, gitRef, ct);
        var source = PrepareSource(tgz); // strip GitHub's top dir; inject static-serve for SPAs

        var token = await GetAccessTokenAsync(ct);
        var bucket = Opt.EffectiveStagingBucket;
        await EnsureBucketAsync(token, bucket, ct);
        var objectName = $"sources/{prototypeId}/{Guid.NewGuid():n}.tgz";
        await UploadObjectAsync(token, bucket, objectName, source, ct);

        var service = ServiceNameFor(prototypeId);
        var image = $"{Opt.Region}-docker.pkg.dev/{Opt.ProjectId}/{Opt.ArtifactRepo}/{service}:{DateTime.UtcNow:yyyyMMddHHmmss}";
        return await CreateBuildAsync(token, bucket, objectName, image, service, ct);
    }

    /// <summary>Check a build once. On Ready, includes the Cloud Run URL.</summary>
    public async Task<(BuildState State, string? Url, string? Error)> CheckBuildAsync(
        string prototypeId, string buildId, CancellationToken ct)
    {
        var token = await GetAccessTokenAsync(ct);
        var (status, detail) = await GetBuildStatusAsync(token, buildId, ct);

        if (!TerminalStatuses.Contains(status)) return (BuildState.Building, null, null);
        if (status != "SUCCESS") return (BuildState.Failed, null, $"Build {status}{(detail is null ? "" : $": {detail}")}");

        var url = await GetServiceUrlAsync(token, ServiceNameFor(prototypeId), ct);
        return (BuildState.Ready, url, null);
    }

    /// <summary>Synchronous build+deploy (used in tests/tools). Blocks until done.</summary>
    public async Task<BuildDeployResult> BuildAndDeployAsync(
        string prototypeId, string repoUrl, string? gitRef, CancellationToken ct)
    {
        var service = ServiceNameFor(prototypeId);
        string buildId;
        try { buildId = await StartBuildAsync(prototypeId, repoUrl, gitRef, ct); }
        catch (Exception e) { return new(false, null, service, null, e.Message); }

        while (true)
        {
            var (state, url, error) = await CheckBuildAsync(prototypeId, buildId, ct);
            if (state == BuildState.Ready) return new(true, url, service, buildId, null);
            if (state == BuildState.Failed) return new(false, null, service, buildId, error);
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
    }

    /// <summary>Delete a prototype's Cloud Run service (teardown). True if gone.</summary>
    public async Task<bool> DeleteServiceAsync(string prototypeId, CancellationToken ct)
    {
        var token = await GetAccessTokenAsync(ct);
        var service = ServiceNameFor(prototypeId);
        var url = $"https://run.googleapis.com/v2/projects/{Opt.ProjectId}/locations/{Opt.Region}/services/{service}";
        using var req = new HttpRequestMessage(HttpMethod.Delete, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var res = await http.SendAsync(req, ct);
        return res.IsSuccessStatusCode || res.StatusCode == HttpStatusCode.NotFound;
    }

    // --- steps --------------------------------------------------------------

    /// <summary>
    /// Re-tar the GitHub archive with the top-level dir removed. If the repo is a
    /// static SPA (has a `build` script, no `start` script, no Procfile), inject
    /// a `Procfile` + zero-dep static server so buildpacks builds it and serves
    /// the output on $PORT with SPA fallback — zero config for the author.
    /// (Buildpacks + Procfile + static-server was verified against a real Vite app.)
    /// </summary>
    private static byte[] PrepareSource(byte[] tgz)
    {
        var entries = new List<(string Name, TarEntryType Type, UnixFileMode Mode, byte[]? Data)>();
        string? packageJson = null;
        var hasProcfile = false;

        using (var inGz = new GZipStream(new MemoryStream(tgz), CompressionMode.Decompress))
        using (var tarIn = new TarReader(inGz))
        {
            while (tarIn.GetNextEntry() is { } entry)
            {
                var slash = entry.Name.IndexOf('/');
                if (slash < 0) continue;               // the top-level dir entry itself
                var name = entry.Name[(slash + 1)..];
                if (name.Length == 0) continue;

                byte[]? data = null;
                if (entry.DataStream is { } ds)
                {
                    using var buf = new MemoryStream();
                    ds.CopyTo(buf);
                    data = buf.ToArray();
                }
                entries.Add((name, entry.EntryType, entry.Mode, data));

                if (name == "package.json" && data is not null) packageJson = System.Text.Encoding.UTF8.GetString(data);
                if (name == "Procfile") hasProcfile = true;
            }
        }

        if (!hasProcfile && IsStaticSpa(packageJson))
        {
            entries.Add(("Procfile", TarEntryType.RegularFile, (UnixFileMode)420,
                System.Text.Encoding.UTF8.GetBytes("web: node proto-static-server.cjs\n")));
            entries.Add(("proto-static-server.cjs", TarEntryType.RegularFile, (UnixFileMode)420,
                System.Text.Encoding.UTF8.GetBytes(StaticServerJs)));
        }

        using var outMs = new MemoryStream();
        using (var outGz = new GZipStream(outMs, CompressionLevel.Optimal, leaveOpen: true))
        using (var tarOut = new TarWriter(outGz, TarEntryFormat.Pax, leaveOpen: true))
        {
            foreach (var e in entries)
            {
                var outEntry = new PaxTarEntry(e.Type, e.Name) { Mode = e.Mode };
                if (e.Data is not null) outEntry.DataStream = new MemoryStream(e.Data);
                tarOut.WriteEntry(outEntry);
            }
        }
        return outMs.ToArray();
    }

    /// <summary>A static SPA = has a `build` script and no `start` script.</summary>
    private static bool IsStaticSpa(string? packageJson)
    {
        if (packageJson is null) return false;
        try
        {
            using var doc = JsonDocument.Parse(packageJson);
            if (!doc.RootElement.TryGetProperty("scripts", out var scripts)) return false;
            return scripts.TryGetProperty("build", out _) && !scripts.TryGetProperty("start", out _);
        }
        catch { return false; }
    }

    // Injected into static-SPA sources. Zero dependencies; serves the built
    // output dir on $PORT with client-side-route fallback.
    private const string StaticServerJs = """
        const http = require('http');
        const fs = require('fs');
        const path = require('path');

        const PORT = process.env.PORT || 8080;
        const CANDIDATES = ['dist', 'build', 'out', 'public'];
        const root =
          CANDIDATES.map((d) => path.resolve(d)).find((d) => {
            try { return fs.statSync(d).isDirectory(); } catch { return false; }
          }) || process.cwd();

        const TYPES = {
          '.html': 'text/html; charset=utf-8', '.js': 'text/javascript', '.mjs': 'text/javascript',
          '.css': 'text/css', '.json': 'application/json', '.svg': 'image/svg+xml',
          '.png': 'image/png', '.jpg': 'image/jpeg', '.jpeg': 'image/jpeg', '.gif': 'image/gif',
          '.webp': 'image/webp', '.ico': 'image/x-icon', '.woff': 'font/woff', '.woff2': 'font/woff2',
          '.ttf': 'font/ttf', '.map': 'application/json', '.txt': 'text/plain', '.wasm': 'application/wasm',
        };

        const indexHtml = path.join(root, 'index.html');

        function sendFile(res, file, status = 200) {
          fs.readFile(file, (err, data) => {
            if (err) { res.writeHead(404); return res.end('Not found'); }
            res.writeHead(status, { 'Content-Type': TYPES[path.extname(file).toLowerCase()] || 'application/octet-stream' });
            res.end(data);
          });
        }

        http.createServer((req, res) => {
          const urlPath = decodeURIComponent((req.url || '/').split('?')[0]);
          let file = path.join(root, urlPath);
          if (path.relative(root, file).startsWith('..')) { res.writeHead(403); return res.end(); }
          fs.stat(file, (err, st) => {
            if (!err && st.isDirectory()) file = path.join(file, 'index.html');
            fs.access(file, fs.constants.R_OK, (e) => {
              if (e) return sendFile(res, indexHtml);
              sendFile(res, file);
            });
          });
        }).listen(PORT, '0.0.0.0', () => console.log(`proto static server on 0.0.0.0:${PORT} serving ${root}`));
        """;

    private async Task EnsureBucketAsync(string token, string bucket, CancellationToken ct)
    {
        using var get = new HttpRequestMessage(HttpMethod.Get, $"https://storage.googleapis.com/storage/v1/b/{bucket}");
        get.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var getRes = await http.SendAsync(get, ct);
        if (getRes.IsSuccessStatusCode) return;
        if (getRes.StatusCode != HttpStatusCode.NotFound) getRes.EnsureSuccessStatusCode();

        using var create = new HttpRequestMessage(
            HttpMethod.Post, $"https://storage.googleapis.com/storage/v1/b?project={Opt.ProjectId}")
        {
            Content = JsonContent.Create(new { name = bucket, location = Opt.Region }),
        };
        create.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var createRes = await http.SendAsync(create, ct);
        createRes.EnsureSuccessStatusCode();
    }

    private async Task UploadObjectAsync(string token, string bucket, string objectName, byte[] bytes, CancellationToken ct)
    {
        var url = $"https://storage.googleapis.com/upload/storage/v1/b/{bucket}/o?uploadType=media&name={Uri.EscapeDataString(objectName)}";
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = new ByteArrayContent(bytes) };
        req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/gzip");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var res = await http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
    }

    private async Task<string> CreateBuildAsync(
        string token, string bucket, string objectName, string image, string service, CancellationToken ct)
    {
        var body = new
        {
            source = new { storageSource = new { bucket, @object = objectName } },
            steps = new object[]
            {
                new { name = "gcr.io/k8s-skaffold/pack",
                      args = new[] { "build", image, "--builder=gcr.io/buildpacks/builder:google-22", "--publish" } },
                new { name = "gcr.io/google.com/cloudsdktool/cloud-sdk", entrypoint = "gcloud",
                      args = new[] { "run", "deploy", service, $"--image={image}", $"--region={Opt.Region}",
                                     "--allow-unauthenticated", "--min-instances=0", "--max-instances=1",
                                     "--cpu=1", "--memory=512Mi" } },
            },
            // No `images` field: `pack --publish` already pushes to the registry.
            // Declaring images makes Cloud Build try a final push from its local
            // daemon (where the image never lands) → spurious PUSH_IMAGE_NOT_FOUND.
            options = new { logging = "CLOUD_LOGGING_ONLY" },
            serviceAccount = $"projects/{Opt.ProjectId}/serviceAccounts/{ServiceAccountEmail()}",
        };

        using var req = new HttpRequestMessage(
            HttpMethod.Post, $"https://cloudbuild.googleapis.com/v1/projects/{Opt.ProjectId}/builds")
        {
            Content = JsonContent.Create(body),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var res = await http.SendAsync(req, ct);
        var payload = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"Cloud Build create failed ({(int)res.StatusCode}): {payload}");

        using var doc = JsonDocument.Parse(payload);
        // Operation.metadata.build.id
        return doc.RootElement.GetProperty("metadata").GetProperty("build").GetProperty("id").GetString()!;
    }

    private async Task<(string Status, string? Detail)> GetBuildStatusAsync(string token, string buildId, CancellationToken ct)
    {
        var url = $"https://cloudbuild.googleapis.com/v1/projects/{Opt.ProjectId}/builds/{buildId}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var res = await http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        var status = root.GetProperty("status").GetString() ?? "UNKNOWN";
        // Prefer the specific failureInfo.detail over the terse statusDetail.
        string? detail = null;
        if (root.TryGetProperty("failureInfo", out var fi) && fi.TryGetProperty("detail", out var fd))
            detail = fd.GetString();
        if (string.IsNullOrEmpty(detail) && root.TryGetProperty("statusDetail", out var sd))
            detail = sd.GetString();
        return (status, detail);
    }

    private async Task<string?> GetServiceUrlAsync(string token, string service, CancellationToken ct)
    {
        var url = $"https://run.googleapis.com/v2/projects/{Opt.ProjectId}/locations/{Opt.Region}/services/{service}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var res = await http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        return doc.RootElement.TryGetProperty("uri", out var uri) ? uri.GetString() : null;
    }
}
