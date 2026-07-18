using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using Microsoft.Extensions.Options;
using Proto.Api.Options;

namespace Proto.Api.Services;

/// <summary>Thin Resend client for the emails the app sends. Typed HttpClient.</summary>
public sealed class ResendClient(HttpClient http, IOptions<ResendOptions> options)
{
    private readonly ResendOptions _opts = options.Value;

    /// <summary>
    /// Send an invite notification. Deliberately contains no auth token — the
    /// invitee just signs in with Google, and the org appears in their discovery
    /// (the invite was recorded as a pending Stytch member). Throws
    /// <see cref="EmailException"/> on a non-2xx from Resend.
    /// </summary>
    public async Task SendInviteEmailAsync(
        string toEmail, string organizationName, string appUrl, CancellationToken ct)
    {
        var org = WebUtility.HtmlEncode(organizationName);
        var subject = $"You're invited to {organizationName} on Proto";
        var html = $"""
            <div style="font-family:system-ui,-apple-system,sans-serif;max-width:480px;color:#1a1a2e">
              <h2 style="margin:0 0 12px">You've been invited to {org} on Proto</h2>
              <p>Sign in with your Google account to accept and join the team.</p>
              <p style="margin:20px 0">
                <a href="{appUrl}" style="display:inline-block;padding:10px 18px;background:#1a1a2e;color:#fff;border-radius:8px;text-decoration:none">Sign in to Proto</a>
              </p>
              <p style="color:#6b7280;font-size:13px">Or go to {appUrl} and sign in with Google using <strong>{WebUtility.HtmlEncode(toEmail)}</strong>.</p>
            </div>
            """;

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails")
        {
            Content = JsonContent.Create(new { from = _opts.FromAddress, to = toEmail, subject, html }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opts.ApiKey);

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new EmailException($"Resend send failed ({(int)response.StatusCode}): {body}");
        }
    }
}

/// <summary>Raised when an outbound email fails to send.</summary>
public sealed class EmailException(string message) : Exception(message);
