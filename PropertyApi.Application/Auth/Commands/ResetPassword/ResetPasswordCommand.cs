using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Application.Auth.Commands.ResetPassword;

public sealed record ResetPasswordCommand(
    string Email,
    string Token,
    string NewPassword,
    string ConfirmPassword,
    string? IpAddress = null) : IRequest<ResetPasswordResult>;

public sealed record ResetPasswordResult
{
    public bool Success { get; init; }
    public bool Conflict { get; init; }
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public static ResetPasswordResult Ok() => new()
    {
        Success = true,
        Message = "Password has been reset successfully."
    };

    public static ResetPasswordResult BadRequest(string message) => new()
    {
        Success = false,
        Message = message
    };

    public static ResetPasswordResult ConflictResult(string message) => new()
    {
        Success = false,
        Conflict = true,
        Message = message
    };

    public static ResetPasswordResult Fail(IEnumerable<string> errors) => new()
    {
        Success = false,
        Errors = errors.ToArray()
    };
}

public sealed class ResetPasswordCommandHandler
    : IRequestHandler<ResetPasswordCommand, ResetPasswordResult>
{
    private readonly UserManager<User> _userManager;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly ILogger<ResetPasswordCommandHandler> _logger;

    public ResetPasswordCommandHandler(
        UserManager<User> userManager,
        IRefreshTokenRepository refreshTokens,
        ILogger<ResetPasswordCommandHandler> logger)
    {
        _userManager = userManager;
        _refreshTokens = refreshTokens;
        _logger = logger;
    }

    public async Task<ResetPasswordResult> Handle(
        ResetPasswordCommand request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Token) ||
            string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return ResetPasswordResult.BadRequest("Email, token, and new password are required.");
        }

        if (!string.Equals(request.NewPassword, request.ConfirmPassword, StringComparison.Ordinal))
            return ResetPasswordResult.BadRequest("New password and confirmation password do not match.");

        var email = NormalizeEmail(request.Email);
        var user = await _userManager.FindByEmailAsync(email);

        if (user is null || user.IsDeleted)
            return ResetPasswordResult.BadRequest("Invalid password reset request.");

        var isSamePassword = await _userManager.CheckPasswordAsync(user, request.NewPassword);
        if (isSamePassword)
            return ResetPasswordResult.ConflictResult("New password must be different from the current password.");

        var token = Uri.UnescapeDataString(request.Token);
        var result = await _userManager.ResetPasswordAsync(user, token, request.NewPassword);

        if (!result.Succeeded)
        {
            var errors = result.Errors.Select(error => error.Description).ToArray();
            _logger.LogWarning(
                "Password reset failed for user {UserId}. Errors: {Errors}",
                user.Id,
                string.Join(", ", errors));

            return ResetPasswordResult.Fail(errors);
        }

        await _userManager.UpdateSecurityStampAsync(user);

        user.UpdatedAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

        await _refreshTokens.RevokeActiveTokensForUserAsync(
            user.Id,
            DateTime.UtcNow,
            request.IpAddress,
            ct);

        _logger.LogInformation("Password reset succeeded for user {UserId}.", user.Id);
        return ResetPasswordResult.Ok();
    }

    private static string NormalizeEmail(string email)
        => email.Trim().ToLowerInvariant();
}

public sealed class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(320).EmailAddress();
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8);
        RuleFor(x => x.ConfirmPassword).Equal(x => x.NewPassword);
    }
}
