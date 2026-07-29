namespace PropertyApi.Application.Auth.Models;

public sealed record AuthenticationSessionRequest(
    IdentityAccountSnapshot Identity,
    string AuthenticationMethod,
    string? IpAddress,
    SuccessfulLoginRecordingMode LoginRecordingMode);

public enum SuccessfulLoginRecordingMode
{
    None,
    BestEffort,
    Required
}

public sealed record AuthenticationSessionResult(
    bool Succeeded,
    string? AccessToken,
    string? RefreshToken,
    DateTime AccessTokenExpiresAtUtc,
    int ExpiresInSeconds,
    string? ErrorCode = null)
{
    public static AuthenticationSessionResult Failed(string errorCode) =>
        new(false, null, null, default, 0, errorCode);
}
