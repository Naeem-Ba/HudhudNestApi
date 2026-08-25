namespace PropertyApi.Application.Auth.Interfaces;

public interface IEmailConfirmationUrlBuilder
{
    string Build(
        Guid identityId,
        string token);
}
