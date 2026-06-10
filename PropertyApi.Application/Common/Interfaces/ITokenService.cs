using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Application.Common.Interfaces;

public interface ITokenService
{
    string GenerateAccessToken(User user, IReadOnlyCollection<string> roles);
    string GenerateRefreshToken();
}
