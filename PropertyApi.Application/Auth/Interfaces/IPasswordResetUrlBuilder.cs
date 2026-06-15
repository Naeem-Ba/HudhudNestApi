namespace PropertyApi.Application.Auth.Interfaces;

public interface IPasswordResetUrlBuilder
{
    string Build(
        string email,
        string token,
        string? requestScheme,
        string? requestHost);
}

