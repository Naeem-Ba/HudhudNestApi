namespace PropertyApi.Application.Auth.Interfaces;

public interface IJwtTokenSettings
{
    int AccessTokenMinutes { get; }
    int RefreshTokenDays { get; }
}
