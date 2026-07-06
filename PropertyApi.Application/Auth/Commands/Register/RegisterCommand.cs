using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Common.Interfaces;
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
    private readonly IIdentityUserService _identityUsers;
    private readonly ILogger<RegisterCommandHandler> _logger;
    private readonly IUnitOfWork _unitOfWork;

    public RegisterCommandHandler(
        IIdentityUserService identityUsers,
        IUnitOfWork unitOfWork,
        ILogger<RegisterCommandHandler> logger)
    {
        _identityUsers = identityUsers;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<RegisterResult> Handle(
        RegisterCommand request,
        CancellationToken ct)
    {
        var email = NormalizeEmail(request.Email);

        if (await _identityUsers.FindByEmailAsync(email, ct) is not null)
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

        await _unitOfWork.BeginTransactionAsync(ct);

        try
        {
            var createResult = await _identityUsers.CreateAsync(
                user,
                request.Password,
                ct);

            if (!createResult.Succeeded)
            {
                await _unitOfWork.RollbackTransactionAsync(ct);

                _logger.LogWarning(
                    "Registration failed for email {Email}. Errors: {Errors}",
                    email,
                    string.Join(", ", createResult.Errors));

                return RegisterResult.Fail(createResult.Errors);
            }

            var roleResult = await _identityUsers.AddToRoleAsync(
                user,
                RoleNames.User,
                ct);

            if (!roleResult.Succeeded)
            {
                await _unitOfWork.RollbackTransactionAsync(ct);

                _logger.LogWarning(
                    "Adding default role failed for user {UserId}. Errors: {Errors}",
                    user.Id,
                    string.Join(", ", roleResult.Errors));

                return RegisterResult.Fail(roleResult.Errors);
            }

            await _unitOfWork.CommitTransactionAsync(ct);

            _logger.LogInformation(
                "User {UserId} registered successfully.",
                user.Id);

            return RegisterResult.Ok(user.Id);
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(
                CancellationToken.None);

            _logger.LogError(
                ex,
                "Atomic registration failed.");

            throw;
        }
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

