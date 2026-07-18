namespace Proto.Api.Options;

/// <summary>
/// Resend email settings. We send invite notifications ourselves via Resend
/// (Stytch's custom email domain is a paid add-on; Resend's domain verification
/// is free). <see cref="FromAddress"/> must be on a domain verified in Resend.
/// Both values are secrets/config — supply via user-secrets / env vars.
/// </summary>
public sealed class ResendOptions
{
    public const string SectionName = "Resend";

    /// <summary>Resend API key (a sending-scoped key is sufficient).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Sender, e.g. invites@yourdomain.com — must be a verified Resend domain.</summary>
    public string FromAddress { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(FromAddress);
}
