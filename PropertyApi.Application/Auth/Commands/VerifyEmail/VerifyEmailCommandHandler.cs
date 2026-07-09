using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.Interfaces;

namespace PropertyApi.Application.Auth.Commands.VerifyEmail;

/// <summary>
/// Confirms an identity email address through the framework-neutral
/// Identity boundary.
///
/// The Application layer works only with identity identifiers and
/// neutral snapshots. ASP.NET Core Identity entities remain hidden
/// inside Infrastructure.
/// </summary>
public sealed class VerifyEmailCommandHandler
    : IRequestHandler<
        VerifyEmailCommand,
        VerifyEmailResult>
{
    private readonly IPureIdentityService _identity;

    private readonly ILogger<VerifyEmailCommandHandler>
        _logger;

    public VerifyEmailCommandHandler(
        IPureIdentityService identity,
        ILogger<VerifyEmailCommandHandler> logger)
    {
        _identity = identity;
        _logger = logger;
    }

    public async Task<VerifyEmailResult> Handle(
        VerifyEmailCommand command,
        CancellationToken ct)
    {
        /*
         * 1. Resolve the identity account.
         */
        var identity =
            await _identity.FindByIdAsync(
                command.UserId,
                ct);

        if (identity is null ||
            identity.IsDeleted)
        {
            _logger.LogWarning(
                "VerifyEmail: Identity not found [{IdentityId}].",
                command.UserId);

            return VerifyEmailResult.Fail(
                "INVALID_TOKEN",
                "رابط التحقق غير صالح أو منتهي الصلاحية.");
        }

        /*
         * 2. Email confirmation is idempotent.
         */
        if (identity.EmailConfirmed)
        {
            _logger.LogInformation(
                "VerifyEmail: Email already confirmed for identity {IdentityId}.",
                identity.IdentityId);

            return VerifyEmailResult.Ok();
        }

        /*
         * 3. An identity without an email cannot be confirmed.
         */
        if (string.IsNullOrWhiteSpace(
                identity.Email))
        {
            _logger.LogWarning(
                "VerifyEmail: No email set for identity {IdentityId}.",
                identity.IdentityId);

            return VerifyEmailResult.Fail(
                "NO_EMAIL",
                "لا يوجد بريد إلكتروني مضاف لهذا الحساب.");
        }

        /*
         * 4. Validate and consume the confirmation token through
         * the framework-neutral Identity boundary.
         */
        var confirmResult =
            await _identity.ConfirmEmailAsync(
                identity.IdentityId,
                command.Token,
                ct);

        if (!confirmResult.Succeeded)
        {
            _logger.LogWarning(
                "VerifyEmail: Confirmation failed for identity {IdentityId}. Errors: {Errors}",
                identity.IdentityId,
                string.Join(
                    ", ",
                    confirmResult.Errors));

            return VerifyEmailResult.Fail(
                "INVALID_TOKEN",
                "رابط التحقق غير صالح أو منتهي الصلاحية. اطلب رابطًا جديدًا.");
        }

        _logger.LogInformation(
            "VerifyEmail: Email {Email} confirmed for identity {IdentityId}.",
            identity.Email,
            identity.IdentityId);

        return VerifyEmailResult.Ok();
    }
}