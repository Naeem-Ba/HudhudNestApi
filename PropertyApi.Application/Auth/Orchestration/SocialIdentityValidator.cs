using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.Commands.SocialLogin;
using PropertyApi.Application.Auth.Contracts;
using PropertyApi.Application.Auth.Models;

namespace PropertyApi.Application.Auth.Orchestration;

public sealed class SocialIdentityValidator
{
    private readonly IReadOnlyCollection<ISocialTokenVerifier> _verifiers;
    private readonly ILogger<SocialIdentityValidator> _logger;

    public SocialIdentityValidator(
        IEnumerable<ISocialTokenVerifier> verifiers,
        ILogger<SocialIdentityValidator> logger)
    {
        _verifiers = verifiers.ToArray();
        _logger = logger;
    }

    public async Task<(VerifiedSocialIdentity? Identity, string? Error)> ValidateAsync(
        SocialLoginCommand command,
        CancellationToken cancellationToken)
    {
        var provider = !string.IsNullOrWhiteSpace(command.GoogleIdToken)
            ? "Google"
            : !string.IsNullOrWhiteSpace(command.AppleIdentityToken)
                ? "Apple"
                : null;
        var token = provider == "Google"
            ? command.GoogleIdToken
            : provider == "Apple"
                ? command.AppleIdentityToken
                : null;

        if (provider is null || string.IsNullOrWhiteSpace(token))
        {
            return (null, "A Google or Apple identity token is required.");
        }

        var verifier = _verifiers.FirstOrDefault(x =>
            x.ProviderName.Equals(provider, StringComparison.OrdinalIgnoreCase));
        if (verifier is null)
        {
            _logger.LogError(
                "No social token verifier is registered. Provider={Provider}",
                provider);
            return (null, "Social login is not configured for this provider.");
        }

        var user = await verifier.VerifyAsync(token, cancellationToken);
        if (user is null)
        {
            _logger.LogWarning(
                "Social token verification failed. Provider={Provider}",
                provider);
            return (null, "The social identity token is invalid or expired.");
        }

        return (new VerifiedSocialIdentity(provider, user), null);
    }
}
