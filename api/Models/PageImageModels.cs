namespace Proto.Api.Models;

// UX-page image upload, done in three legs: (1) the browser asks the API for a
// signed upload URL, (2) it PUTs the bytes straight to Supabase Storage, (3) it
// tells the API the upload landed so the API records the image on the page.

public sealed record UploadUrlRequest(string ContentType);

public sealed record UploadUrlResponse(
    string UploadUrl,   // full URL to PUT the bytes to (token embedded)
    string Token,       // send as `Authorization: Bearer` on the PUT
    string Path,        // storage object path (echo back on confirm)
    string PublicUrl);  // where the image will be served from

// Width/height come from the browser (it decodes the image before upload). Path
// must be the one the API issued, so it stays bound to this page.
public sealed record ConfirmImageRequest(string Path, int Width, int Height);
