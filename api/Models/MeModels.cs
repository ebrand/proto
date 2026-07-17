namespace Proto.Api.Models;

// Shape returned by GET /api/me: the current caller resolved to their Proto
// user + tenant (mapped from the Stytch session via the sync keys).

public sealed record MeResponse(MeUser User, MeTenant Tenant);

public sealed record MeUser(
    string Id,
    string Email,
    string DisplayName,
    string Status,
    string TenantRole);

public sealed record MeTenant(
    string Id,
    string Name,
    string Slug,
    string SubscriptionTierCode,
    string SubscriptionStatus);
