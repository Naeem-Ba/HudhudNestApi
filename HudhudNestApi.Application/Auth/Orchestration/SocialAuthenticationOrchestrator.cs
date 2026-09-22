using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Auth.Abstractions;
using HudhudNestApi.Application.Auth.Commands.SocialLogin;
using HudhudNestApi.Application.Common.Observability;

namespace HudhudNestApi.Application.Auth.Orchestration;

public sealed class SocialAuthenticationOrchestrator : ISocialAuthenticationOrchestrator
{
    private readonly SocialIdentityValidator _validator;
    private readonly SocialAccountResolver _resolver;
    private readonly SocialAccountMutationCoordinator _mutations;
    private readonly ILogger<SocialAuthenticationOrchestrator> _logger;

    public SocialAuthenticationOrchestrator(
        SocialIdentityValidator validator,
        SocialAccountResolver resolver,
        SocialAccountMutationCoordinator mutations,
        ILogger<SocialAuthenticationOrchestrator> logger)
    {
        _validator = validator;
        _resolver = resolver;
        _mutations = mutations;
        _logger = logger;
    }

    public async Task<SocialLoginResult> AuthenticateAsync(
        SocialLoginCommand command,
        CancellationToken cancellationToken)
    {
        var operationId = Guid.NewGuid().ToString("N");
        var validation = await _validator.ValidateAsync(command, cancellationToken);
        if (validation.Identity is null)
        {
            _logger.LogWarning(
                "Social authentication rejected. OperationId={OperationId}",
                operationId);
            return SocialLoginResult.Failed(validation.Error!);
        }

        var resolution = await _resolver.ResolveAsync(
            validation.Identity,
            cancellationToken);
        ApplicationTelemetry.RecordAuthenticationStage(
            "account_resolution",
            resolution.Kind.ToString().ToLowerInvariant(),
            "social");
        var result = await _mutations.CompleteAsync(
            resolution,
            command,
            cancellationToken);
        _logger.LogInformation(
            "Social authentication completed. OperationId={OperationId}, Provider={Provider}, Result={Result}",
            operationId,
            validation.Identity.Provider,
            result.Success ? "success" : "failed");
        return result;
    }
}
