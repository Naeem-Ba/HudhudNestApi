using HudhudNestApi.Application.Auth.Models;

namespace HudhudNestApi.Application.Auth.Abstractions;

public interface IAuthenticationSessionIssuer
{
    Task<AuthenticationSessionResult> IssueAsync(
        AuthenticationSessionRequest request,
        CancellationToken cancellationToken);
}
