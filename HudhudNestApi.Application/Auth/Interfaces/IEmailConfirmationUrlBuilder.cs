namespace HudhudNestApi.Application.Auth.Interfaces;

public interface IEmailConfirmationUrlBuilder
{
    string Build(
        Guid identityId,
        string token);
}
