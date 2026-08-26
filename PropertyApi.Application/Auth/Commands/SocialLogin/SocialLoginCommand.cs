using MediatR;

namespace PropertyApi.Application.Auth.Commands.SocialLogin;

public sealed record SocialLoginCommand(
    string? GoogleIdToken,
    string? AppleIdentityToken,
    string? AppleAuthorizationCode,
    string? AppleFirstName,
    string? AppleLastName,
    string? AppleNonce = null,
    string? IpAddress = null) : IRequest<SocialLoginResult>;

public sealed record SocialLoginResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string AccessToken { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
    public int ExpiresIn { get; init; }

    public static SocialLoginResult Ok(string accessToken, string refreshToken, int expiresIn) => new()
    {
        Success = true,
        AccessToken = accessToken,
        RefreshToken = refreshToken,
        ExpiresIn = expiresIn
    };

    public static SocialLoginResult Failed(string message) => new()
    {
        Success = false,
        Message = message
    };
}
