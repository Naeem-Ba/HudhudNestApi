using PropertyApi.Application.Auth.Models;

namespace PropertyApi.Application.Common.Interfaces;

public interface ITokenService
{
    string GenerateAccessToken(
        AccessTokenSubject subject,
        IReadOnlyCollection<string> roles);

    string GenerateRefreshToken();

    DateTime GetAccessTokenExpiresAtUtc();
}