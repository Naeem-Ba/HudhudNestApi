using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.DTOs;
using PropertyApi.Application.Auth.Interfaces;

namespace PropertyApi.Application.Auth.Commands.AddEmail;

// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
// Ø¥Ø¶Ø§ÙØ© Ø§Ù„Ø¨Ø±ÙŠØ¯ Ø§Ù„Ø¥Ù„ÙƒØªØ±ÙˆÙ†ÙŠ Ù„Ø­Ø³Ø§Ø¨ Ù…ÙˆØ¬ÙˆØ¯
//
// Ø§Ù„Ø³ÙŠÙ†Ø§Ø±ÙŠÙˆ:
// Ø§Ù„Ù…Ø³ØªØ®Ø¯Ù… Ø³Ø¬Ù‘Ù„ Ø¨Ù‡Ø§ØªÙÙ‡ ÙÙ‚Ø·. Ø¨Ø¹Ø¯ Ø£Ø³Ø¨ÙˆØ¹ ÙŠØ±ÙŠØ¯ Ø¥Ø¶Ø§ÙØ© Ø¨Ø±ÙŠØ¯Ù‡
// Ø§Ù„Ø¥Ù„ÙƒØªØ±ÙˆÙ†ÙŠ Ù„ÙŠØªÙ…ÙƒÙ† Ù…Ù† Ø§Ù„Ø¯Ø®ÙˆÙ„ Ø¨Ø·Ø±ÙŠÙ‚ØªÙŠÙ†.
//
// Clean Architecture:
// Ù‡Ø°Ø§ Ø§Ù„Ù€ Handler Ù„Ø§ ÙŠØ¹ØªÙ…Ø¯ Ø¹Ù„Ù‰ concrete identity user service Ù…Ø¨Ø§Ø´Ø±Ø©.
// ÙŠØ³ØªØ®Ø¯Ù… IIdentityUserService ÙÙ‚Ø·ØŒ ÙˆØ§Ù„ØªÙ†ÙÙŠØ° Ø§Ù„Ø­Ù‚ÙŠÙ‚ÙŠ ÙÙŠ Infrastructure.
// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

public sealed record AddEmailCommand(
    Guid UserId,
    string Email
) : IRequest<AddEmailResult>;

public sealed class AddEmailCommandHandler
    : IRequestHandler<AddEmailCommand, AddEmailResult>
{
    private readonly IIdentityUserService _identityUsers;
    private readonly IEmailVerificationService _emailService;
    private readonly ILogger<AddEmailCommandHandler> _logger;

    public AddEmailCommandHandler(
        IIdentityUserService identityUsers,
        IEmailVerificationService emailService,
        ILogger<AddEmailCommandHandler> logger)
    {
        _identityUsers = identityUsers;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<AddEmailResult> Handle(
        AddEmailCommand request,
        CancellationToken ct)
    {
        var email = NormalizeEmail(request.Email);

        // â”€â”€ 1. ØªØ­Ù‚Ù‚ Ù…Ù† ÙˆØ¬ÙˆØ¯ Ø§Ù„Ù…Ø³ØªØ®Ø¯Ù… â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var user = await _identityUsers.FindByIdAsync(request.UserId, ct);

        if (user is null || user.IsDeleted)
            return AddEmailResult.Fail("USER_NOT_FOUND", "Ø§Ù„Ù…Ø³ØªØ®Ø¯Ù… ØºÙŠØ± Ù…ÙˆØ¬ÙˆØ¯.");

        // â”€â”€ 2. Ù‡Ù„ Ø§Ù„Ø¨Ø±ÙŠØ¯ Ù…ÙØ¶Ø§Ù Ø¨Ø§Ù„ÙØ¹Ù„ØŸ â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        if (!string.IsNullOrWhiteSpace(user.Email) && user.EmailConfirmed)
            return AddEmailResult.Fail(
                "EMAIL_ALREADY_SET",
                "Ù„Ø¯ÙŠÙƒ Ø¨Ø±ÙŠØ¯ Ø¥Ù„ÙƒØªØ±ÙˆÙ†ÙŠ Ù…ÙÙØ¹ÙŽÙ‘Ù„ Ø¨Ø§Ù„ÙØ¹Ù„.");

        // â”€â”€ 3. Ù‡Ù„ Ø§Ù„Ø¨Ø±ÙŠØ¯ Ù…Ø³ØªØ®Ø¯Ù… Ù…Ù† Ù‚Ø¨Ù„ Ø´Ø®Øµ Ø¢Ø®Ø±ØŸ â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var existingWithEmail = await _identityUsers.FindByEmailAsync(email, ct);

        if (existingWithEmail is not null && existingWithEmail.Id != user.Id)
            return AddEmailResult.Fail(
                "EMAIL_TAKEN",
                "Ù‡Ø°Ø§ Ø§Ù„Ø¨Ø±ÙŠØ¯ Ø§Ù„Ø¥Ù„ÙƒØªØ±ÙˆÙ†ÙŠ Ù…Ø³ØªØ®Ø¯Ù… Ù…Ù† Ù‚Ø¨Ù„ Ø­Ø³Ø§Ø¨ Ø¢Ø®Ø±.");

        // â”€â”€ 4. Ø£Ø¶Ù Ø§Ù„Ø¨Ø±ÙŠØ¯ (ØºÙŠØ± Ù…ÙÙØ¹ÙŽÙ‘Ù„ Ø¨Ø¹Ø¯) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var setEmailResult = await _identityUsers.SetEmailAsync(user, email, ct);

        if (!setEmailResult.Succeeded)
        {
            _logger.LogError(
                "Failed to set email for user {UserId}: {Errors}",
                request.UserId,
                string.Join(", ", setEmailResult.Errors));

            return AddEmailResult.Fail("EMAIL_SET_FAILED", "ÙØ´Ù„ Ø¥Ø¶Ø§ÙØ© Ø§Ù„Ø¨Ø±ÙŠØ¯.");
        }

        // â”€â”€ 5. Ø£Ù†Ø´Ø¦ Ø±Ù…Ø² Ø§Ù„ØªØ­Ù‚Ù‚ â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var token = await _identityUsers.GenerateEmailConfirmationTokenAsync(user, ct);

        // â”€â”€ 6. Ø£Ø±Ø³Ù„ Ø±Ø³Ø§Ù„Ø© Ø§Ù„ØªØ­Ù‚Ù‚ â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        await _emailService.SendVerificationLinkAsync(email, token, ct);

        _logger.LogInformation(
            "Email verification sent to {Email} for user {UserId}",
            email,
            request.UserId);

        return AddEmailResult.Ok();
    }

    private static string NormalizeEmail(string email)
        => email.Trim().ToLowerInvariant();
}

public sealed class AddEmailCommandValidator
    : AbstractValidator<AddEmailCommand>
{
    public AddEmailCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("Ù…Ø¹Ø±Ù‘Ù Ø§Ù„Ù…Ø³ØªØ®Ø¯Ù… Ù…Ø·Ù„ÙˆØ¨.");

        RuleFor(x => x.Email)
            .NotEmpty()
            .MaximumLength(320)
            .EmailAddress()
            .Must(email => email is not null && !email.Any(char.IsWhiteSpace))
            .WithMessage("Email must not contain whitespace.");
    }
}

