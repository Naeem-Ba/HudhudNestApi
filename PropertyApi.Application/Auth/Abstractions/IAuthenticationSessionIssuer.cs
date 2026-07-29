using PropertyApi.Application.Auth.Models;

namespace PropertyApi.Application.Auth.Abstractions;

public interface IAuthenticationSessionIssuer
{
    Task<AuthenticationSessionResult> IssueAsync(
        AuthenticationSessionRequest request,
        CancellationToken cancellationToken);
}
