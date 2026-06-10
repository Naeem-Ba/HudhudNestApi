using Microsoft.Extensions.Options;
using PropertyApi.Application.Auth.Interfaces;

namespace PropertyApi.Infrastructure.Identity.Services;

public sealed class JwtTokenSettings : IJwtTokenSettings
{
    private readonly JwtOptions _options;

    public JwtTokenSettings(IOptions<JwtOptions> options)
        => _options = options.Value;

    public int AccessTokenMinutes => _options.AccessTokenMinutes;
    public int RefreshTokenDays => _options.RefreshTokenDays;
}
