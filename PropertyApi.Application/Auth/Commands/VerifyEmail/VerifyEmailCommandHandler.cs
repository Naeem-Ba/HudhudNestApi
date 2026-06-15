using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.Interfaces;

namespace PropertyApi.Application.Auth.Commands.VerifyEmail;

/// <summary>
/// Ù…Ø¹Ø§Ù„Ø¬ ØªÙØ¹ÙŠÙ„ Ø§Ù„Ø¨Ø±ÙŠØ¯ Ø§Ù„Ø¥Ù„ÙƒØªØ±ÙˆÙ†ÙŠ.
/// ÙŠØ¹ØªÙ…Ø¯ Ø¹Ù„Ù‰ IIdentityUserService Ø¨Ø¯Ù„Ø§Ù‹ Ù…Ù† concrete identity user service Ø­ØªÙ‰ Ù„Ø§ ØªØ¹Ø±Ù Application ØªÙØ§ØµÙŠÙ„ ASP.NET Identity.
/// </summary>
public sealed class VerifyEmailCommandHandler
    : IRequestHandler<VerifyEmailCommand, VerifyEmailResult>
{
    private readonly IIdentityUserService _identityUsers;
    private readonly ILogger<VerifyEmailCommandHandler> _logger;

    public VerifyEmailCommandHandler(
        IIdentityUserService identityUsers,
        ILogger<VerifyEmailCommandHandler> logger)
    {
        _identityUsers = identityUsers;
        _logger = logger;
    }

    public async Task<VerifyEmailResult> Handle(
        VerifyEmailCommand command,
        CancellationToken ct)
    {
        // â”€â”€ 1. Ø§Ø¨Ø­Ø« Ø¹Ù† Ø§Ù„Ù…Ø³ØªØ®Ø¯Ù… â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var user = await _identityUsers.FindByIdAsync(command.UserId, ct);

        if (user is null || user.IsDeleted)
        {
            _logger.LogWarning(
                "VerifyEmail: User not found [{UserId}]",
                command.UserId);

            return VerifyEmailResult.Fail(
                "INVALID_TOKEN",
                "Ø±Ø§Ø¨Ø· Ø§Ù„ØªØ­Ù‚Ù‚ ØºÙŠØ± ØµØ§Ù„Ø­ Ø£Ùˆ Ù…Ù†ØªÙ‡ÙŠ Ø§Ù„ØµÙ„Ø§Ø­ÙŠØ©.");
        }

        // â”€â”€ 2. Ù‡Ù„ Ø§Ù„Ø¨Ø±ÙŠØ¯ Ù…ÙÙØ¹ÙŽÙ‘Ù„ Ø¨Ø§Ù„ÙØ¹Ù„ØŸ â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        if (user.EmailConfirmed)
        {
            _logger.LogInformation(
                "VerifyEmail: Email already confirmed for {UserId}",
                command.UserId);

            return VerifyEmailResult.Ok();
        }

        // â”€â”€ 3. ØªØ£ÙƒØ¯ Ù…Ù† ÙˆØ¬ÙˆØ¯ Ø¨Ø±ÙŠØ¯ Ø¥Ù„ÙƒØªØ±ÙˆÙ†ÙŠ â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        if (string.IsNullOrWhiteSpace(user.Email))
        {
            _logger.LogWarning(
                "VerifyEmail: No email set for user {UserId}",
                command.UserId);

            return VerifyEmailResult.Fail(
                "NO_EMAIL",
                "Ù„Ø§ ÙŠÙˆØ¬Ø¯ Ø¨Ø±ÙŠØ¯ Ø¥Ù„ÙƒØªØ±ÙˆÙ†ÙŠ Ù…ÙØ¶Ø§Ù Ù„Ù‡Ø°Ø§ Ø§Ù„Ø­Ø³Ø§Ø¨.");
        }

        // â”€â”€ 4. Ø§Ù„ØªØ­Ù‚Ù‚ Ù…Ù† Ø§Ù„Ø±Ù…Ø² Ø¹Ø¨Ø± Infrastructure Identity â”€â”€â”€â”€â”€
        var confirmResult = await _identityUsers.ConfirmEmailAsync(
            user,
            command.Token,
            ct);

        if (!confirmResult.Succeeded)
        {
            _logger.LogWarning(
                "VerifyEmail: Confirmation failed for {UserId}. Errors: {Errors}",
                command.UserId,
                string.Join(", ", confirmResult.Errors));

            return VerifyEmailResult.Fail(
                "INVALID_TOKEN",
                "Ø±Ø§Ø¨Ø· Ø§Ù„ØªØ­Ù‚Ù‚ ØºÙŠØ± ØµØ§Ù„Ø­ Ø£Ùˆ Ù…Ù†ØªÙ‡ÙŠ Ø§Ù„ØµÙ„Ø§Ø­ÙŠØ©. Ø§Ø·Ù„Ø¨ Ø±Ø§Ø¨Ø·Ø§Ù‹ Ø¬Ø¯ÙŠØ¯Ø§Ù‹.");
        }

        _logger.LogInformation(
            "VerifyEmail: Email {Email} confirmed for user {UserId}",
            user.Email,
            command.UserId);

        return VerifyEmailResult.Ok();
    }
}

