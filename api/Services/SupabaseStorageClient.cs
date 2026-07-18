using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Proto.Api.Options;

namespace Proto.Api.Services;

/// <summary>
/// Thin client over the Supabase Storage REST API for UX-page images. The API
/// is the trust boundary: it mints short-lived signed upload URLs (the browser
/// PUTs the bytes straight to storage) and deletes objects. All admin calls
/// authenticate with the Supabase secret key sent as BOTH an Authorization
/// bearer and an `apikey` header — storage rejects the new sb_secret_ keys on
/// some endpoints unless `apikey` is present.
/// </summary>
public sealed class SupabaseStorageClient(HttpClient http, IOptions<SupabaseOptions> options)
{
    private SupabaseOptions Opt => options.Value;

    public bool IsConfigured => Opt.StorageConfigured;

    /// <summary>The public URL an uploaded object is served from.</summary>
    public string PublicUrl(string path) =>
        $"{Base}/storage/v1/object/public/{Opt.StorageBucket}/{path}";

    private string Base => Opt.Url.TrimEnd('/');

    private HttpRequestMessage Admin(HttpMethod method, string relativeUrl)
    {
        var req = new HttpRequestMessage(method, $"{Base}/storage/v1/{relativeUrl}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Opt.SecretKey);
        req.Headers.TryAddWithoutValidation("apikey", Opt.SecretKey);
        return req;
    }

    /// <summary>
    /// Create a signed upload URL for <paramref name="path"/>. Returns the full
    /// URL the browser PUTs to (with the token embedded) plus the raw token —
    /// the PUT must send the token as an Authorization bearer.
    /// </summary>
    public async Task<(string UploadUrl, string Token)> CreateSignedUploadUrlAsync(
        string path, CancellationToken ct)
    {
        using var req = Admin(HttpMethod.Post, $"object/upload/sign/{Opt.StorageBucket}/{path}");
        using var res = await http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        var relative = root.GetProperty("url").GetString()
            ?? throw new InvalidOperationException("sign response missing url");
        var token = root.GetProperty("token").GetString()
            ?? throw new InvalidOperationException("sign response missing token");

        // `relative` looks like /object/upload/sign/{bucket}/{path}?token=...
        return ($"{Base}/storage/v1{relative}", token);
    }

    /// <summary>Delete an object. True on success or if already gone.</summary>
    public async Task<bool> DeleteAsync(string path, CancellationToken ct)
    {
        using var req = Admin(HttpMethod.Delete, $"object/{Opt.StorageBucket}/{path}");
        using var res = await http.SendAsync(req, ct);
        return res.IsSuccessStatusCode || res.StatusCode == System.Net.HttpStatusCode.NotFound;
    }
}
