using System.Text.Json.Serialization;

namespace NoraPremium.Service;

internal sealed record ActivationRequest(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("installation_id")] string InstallationId,
    [property: JsonPropertyName("app_version")] string AppVersion);

internal sealed record RefreshRequest(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("installation_id")] string InstallationId,
    [property: JsonPropertyName("app_version")] string AppVersion);

internal sealed record PremiumTokenPayload(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("license_id")] string LicenseId,
    [property: JsonPropertyName("activation_id")] string ActivationId,
    [property: JsonPropertyName("installation_id")] string InstallationId,
    [property: JsonPropertyName("edition")] string Edition,
    [property: JsonPropertyName("entitlements")] string[] Entitlements,
    [property: JsonPropertyName("issued_at")] long IssuedAt,
    [property: JsonPropertyName("refresh_after")] long RefreshAfter,
    [property: JsonPropertyName("valid_until")] long ValidUntil,
    [property: JsonPropertyName("key_id")] string KeyId);

internal sealed record TokenResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("valid_until")] long ValidUntil,
    [property: JsonPropertyName("entitlements")] string[] Entitlements);

internal sealed record ApiError(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("message")] string Message);

internal sealed record LicenseRecord(
    string Id,
    string CodeLast4,
    string Note,
    int MaxDevices,
    string CreatedAt,
    string? ExpiresAt,
    string? RevokedAt,
    int ActiveDevices,
    int TotalDevices);

internal sealed record ActivationRecord(
    string Id,
    string LicenseId,
    string CodeLast4,
    string InstallationLabel,
    string AppVersion,
    string FirstSeenAt,
    string LastSeenAt,
    string? RevokedAt);

internal sealed record AuditRecord(string CreatedAt, string Action, string Detail, string RemoteIp);

internal sealed record DashboardData(
    int TotalLicenses,
    int ActiveLicenses,
    int RevokedLicenses,
    int ActiveDevices,
    IReadOnlyList<LicenseRecord> Licenses,
    IReadOnlyList<ActivationRecord> Activations,
    IReadOnlyList<AuditRecord> Audit);

internal sealed record ActivationGrant(string LicenseId, string ActivationId);
