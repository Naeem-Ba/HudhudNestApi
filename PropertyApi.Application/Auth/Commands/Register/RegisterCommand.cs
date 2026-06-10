using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using PropertyApi.Domain.Users.Constants;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Application.Auth.Commands.Register;

public sealed record RegisterCommand(
    string FirstName,
    string LastName,
    string Email,
    string Password) : IRequest<RegisterResult>;

public sealed record RegisterResult
{
    public bool Success { get; init; }
    public bool Conflict { get; init; }
    public string Message { get; init; } = string.Empty;
    public Guid? UserId { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public static RegisterResult Ok(Guid userId) => new()
    {
        Success = true,
        UserId = userId,
        Message = "Registration successful."
    };

    public static RegisterResult EmailConflict() => new()
    {
        Success = false,
        Conflict = true,
        Message = "Email already registered."
    };

    public static RegisterResult Fail(IEnumerable<string> errors) => new()
    {
        Success = false,
        Errors = errors.ToArray()
    };
}

public sealed class RegisterCommandHandler
    : IRequestHandler<RegisterCommand, RegisterResult>
{
    private readonly UserManager<User> _userManager;
    private readonly ILogger<RegisterCommandHandler> _logger;

    public RegisterCommandHandler(
        UserManager<User> userManager,
        ILogger<RegisterCommandHandler> logger)
    {
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<RegisterResult> Handle(
        RegisterCommand request,
        CancellationToken ct)
    {
        var email = NormalizeEmail(request.Email);

        if (await _userManager.FindByEmailAsync(email) is not null)
            return RegisterResult.EmailConflict();

        var user = new User
        {
            UserName = email,
            Email = email,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var createResult = await _userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            var errors = createResult.Errors.Select(e => e.Description).ToArray();
            _logger.LogWarning(
                "Registration failed for email {Email}. Errors: {Errors}",
                email,
                string.Join(", ", errors));

            return RegisterResult.Fail(errors);
        }

        var roleResult = await _userManager.AddToRoleAsync(user, RoleNames.User);
        if (!roleResult.Succeeded)
        {
            var errors = roleResult.Errors.Select(e => e.Description).ToArray();
            _logger.LogWarning(
                "Adding default role failed for user {UserId}. Errors: {Errors}",
                user.Id,
                string.Join(", ", errors));

            return RegisterResult.Fail(errors);
        }

        _logger.LogInformation("User {UserId} registered with email {Email}.", user.Id, email);
        return RegisterResult.Ok(user.Id);
    }

    private static string NormalizeEmail(string email)
        => email.Trim().ToLowerInvariant();
}

public sealed class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email)
            .NotEmpty()
            .MaximumLength(320)
            .EmailAddress()
            .Must(email => email is not null && !email.Any(char.IsWhiteSpace))
            .WithMessage("Email must not contain whitespace.");
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8);
    }
}
