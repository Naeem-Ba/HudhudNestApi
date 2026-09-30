using HudhudNestApi.Application.Auth.Models;

namespace HudhudNestApi.Application.Common.Interfaces;

public interface ITokenService
{
    string GenerateAccessToken(
        AccessTokenSubject subject,
        IReadOnlyCollection<string> roles);

    string GenerateRefreshToken();

    DateTime GetAccessTokenExpiresAtUtc();
}