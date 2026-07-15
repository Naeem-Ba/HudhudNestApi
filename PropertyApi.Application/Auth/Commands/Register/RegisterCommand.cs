using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Auth.Models;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Users.Interfaces;
using PropertyApi.Domain.Users.Constants;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Application.Auth.Commands.Register;

public sealed record RegisterCommand(
    string FirstName,
    string LastName,
    string Email,
    string Password)
    : IRequest<RegisterResult>;

public sealed record RegisterResult
{
    public bool Success { get; init; }

    public bool Conflict { get; init; }

    public string Message { get; init; }
        = string.Empty;

    public Guid? UserId { get; init; }

    public IReadOnlyList<string> Errors { get; init; }
        = Array.Empty<string>();

    public static RegisterResult Ok(
        Guid userId)
        => new()
        {
            Success = true,
            UserId = userId,
            Message = "Registration successful."
        };

    public static RegisterResult EmailConflict()
        => new()
        {
            Success = false,
            Conflict = true,
            Message = "Email already registered."
        };

    public static RegisterResult Fail(
        IEnumerable<string> errors)
        => new()
        {
            Success = false,
            Errors = errors.ToArray()
        };
}

public sealed class RegisterCommandHandler
    : IRequestHandler<
        RegisterCommand,
        RegisterResult>
{
    private readonly IRegisterIdentityService _identity;

    private readonly IUserAccountRepository _accounts;

    private readonly IUnitOfWork _unitOfWork;

    private readonly ILogger<RegisterCommandHandler>
        _logger;

    public RegisterCommandHandler(
        IRegisterIdentityService identity,
        IUserAccountRepository accounts,
        IUnitOfWork unitOfWork,
        ILogger<RegisterCommandHandler> logger)
    {
        _identity = identity;
        _accounts = accounts;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<RegisterResult> Handle(
        RegisterCommand request,
        CancellationToken ct)
    {
        var email =
            NormalizeEmail(
                request.Email);

        /*
         * Fast conflict check.
         *
         * The database unique constraint remains the final authority
         * for concurrent requests.
         */
        var existingIdentity =
            await _identity.FindByEmailAsync(
                email,
                ct);

        if (existingIdentity is not null)
        {
            return RegisterResult.EmailConflict();
        }

        var now =
            DateTime.UtcNow;

        var userId =
            Guid.NewGuid();

        /*
         * Business profile aggregate.
         *
         * During the migration period:
         *
         * IdentityId == UserAccountId
         */
        var account =
            UserAccount.Create(
                userId,
                request.FirstName,
                request.LastName,
                now);

        /*
         * Framework-neutral identity creation request.
         *
         * Legacy profile fields are temporary compatibility data.
         * They can be removed after the remaining Legacy User readers
         * are migrated.
         */
        var identityRequest =
            new CreateIdentityAccount(
                UserAccountId:
                    userId,

                Email:
                    email,

                PhoneNumber:
                    null,

                Password:
                    request.Password,

                LegacyFirstName:
                    request.FirstName.Trim(),

                LegacyLastName:
                    request.LastName.Trim(),

                LegacyCreatedAtUtc:
                    now);

        await _unitOfWork
            .BeginTransactionAsync(ct);

        try
        {
            /*
             * 1. Create Identity account.
             */
            var createResult =
                await _identity.CreateAsync(
                    identityRequest,
                    ct);

            if (!createResult.Succeeded)
            {
                await _unitOfWork
                    .RollbackTransactionAsync(ct);

                _logger.LogWarning(
                    "Registration failed for email {Email}. Errors: {Errors}",
                    email,
                    string.Join(
                        ", ",
                        createResult.Errors));

                return RegisterResult.Fail(
                    createResult.Errors);
            }

            /*
             * 2. Assign the default role.
             */
            var roleResult =
                await _identity.AddToRoleAsync(
                    userId,
                    RoleNames.User,
                    ct);

            if (!roleResult.Succeeded)
            {
                await _unitOfWork
                    .RollbackTransactionAsync(ct);

                _logger.LogWarning(
                    "Adding default role failed for identity {IdentityId}. Errors: {Errors}",
                    userId,
                    string.Join(
                        ", ",
                        roleResult.Errors));

                return RegisterResult.Fail(
                    roleResult.Errors);
            }

            /*
             * 3. Track the business profile.
             */
            await _accounts.AddAsync(
                account,
                ct);

            /*
             * UnitOfWork.CommitTransactionAsync does not implicitly
             * call SaveChangesAsync.
             */
            await _unitOfWork
                .SaveChangesAsync(ct);

            /*
             * 4. Commit Identity + role + UserAccount atomically.
             */
            await _unitOfWork
                .CommitTransactionAsync(ct);

            _logger.LogInformation(
                "Identity {IdentityId} and UserAccount registered successfully.",
                userId);

            return RegisterResult.Ok(
                userId);
        }
        catch (Exception ex)
        {
            await _unitOfWork
                .RollbackTransactionAsync(
                    CancellationToken.None);

            _logger.LogError(
                ex,
                "Atomic registration failed.");

            throw;
        }
    }

    private static string NormalizeEmail(
        string email)
        => email
            .Trim()
            .ToLowerInvariant();
}

public sealed class RegisterCommandValidator
    : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        RuleFor(x => x.FirstName)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.LastName)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.Email)
            .NotEmpty()
            .MaximumLength(320)
            .EmailAddress()
            .Must(
                email =>
                    email != null &&
                    !email.Any(
                        char.IsWhiteSpace))
            .WithMessage(
                "Email must not contain whitespace.");

        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(8);
    }
}
