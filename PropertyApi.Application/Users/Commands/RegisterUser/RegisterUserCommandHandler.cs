using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Users.DTOs;
using PropertyApi.Domain.Users.Constants;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Application.Users.Commands.RegisterUser;

public sealed class RegisterUserCommandHandler
    : IRequestHandler<RegisterUserCommand, UserDto>
{
    private const string DefaultRole = RoleNames.User;

    private readonly IIdentityUserService _identityUsers;
    private readonly IIdentityRoleService _identityRoles;
    private readonly ILogger<RegisterUserCommandHandler> _logger;

    public RegisterUserCommandHandler(
        IIdentityUserService identityUsers,
        IIdentityRoleService identityRoles,
        ILogger<RegisterUserCommandHandler> logger)
    {
        _identityUsers = identityUsers;
        _identityRoles = identityRoles;
        _logger = logger;
    }

    public async Task<UserDto> Handle(
        RegisterUserCommand request,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        var existingUser = await _identityUsers.FindByEmailAsync(email, cancellationToken);
        if (existingUser is not null)
            throw new ConflictException("Email already registered.");

        var now = DateTime.UtcNow;
        var user = new User
        {
            UserName = email,
            Email = email,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName)
                ? null
                : request.DisplayName.Trim(),
            PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber)
                ? null
                : request.PhoneNumber.Trim(),
            PreferredLanguage = request.PreferredLanguage.Trim().ToLowerInvariant(),
            PreferredCurrency = request.PreferredCurrency.Trim().ToUpperInvariant(),
            CountryCode = string.IsNullOrWhiteSpace(request.CountryCode)
                ? null
                : request.CountryCode.Trim().ToUpperInvariant(),
            CreatedAt = now,
            UpdatedAt = now
        };

        var createResult = await _identityUsers.CreateAsync(
            user,
            request.Password,
            cancellationToken);

        if (!createResult.Succeeded)
        {
            _logger.LogWarning(
                "User registration failed for email {Email}. Errors: {Errors}",
                email,
                string.Join(", ", createResult.Errors));

            throw new InvalidOperationException(ToErrorMessage(createResult.Errors));
        }

        if (!await _identityRoles.RoleExistsAsync(DefaultRole, cancellationToken))
        {
            var createRoleResult = await _identityRoles.CreateRoleAsync(DefaultRole, cancellationToken);
            if (!createRoleResult.Succeeded)
            {
                _logger.LogWarning(
                    "Default role creation failed. Role={Role}. Errors: {Errors}",
                    DefaultRole,
                    string.Join(", ", createRoleResult.Errors));

                throw new InvalidOperationException(ToErrorMessage(createRoleResult.Errors));
            }
        }

        var roleResult = await _identityUsers.AddToRoleAsync(user, DefaultRole, cancellationToken);
        if (!roleResult.Succeeded)
        {
            _logger.LogWarning(
                "Adding default role failed for user {UserId}. Errors: {Errors}",
                user.Id,
                string.Join(", ", roleResult.Errors));

            throw new InvalidOperationException(ToErrorMessage(roleResult.Errors));
        }

        return new UserDto
        {
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            FirstName = user.FirstName,
            LastName = user.LastName,
            DisplayName = user.DisplayName,
            PhoneNumber = user.PhoneNumber,
            ProfileImageUrl = user.ProfileImageUrl,
            PreferredLanguage = user.PreferredLanguage,
            PreferredCurrency = user.PreferredCurrency,
            CountryCode = user.CountryCode,
            EmailConfirmed = user.EmailConfirmed,
            CreatedAt = user.CreatedAt,
            Roles = new[] { DefaultRole }
        };
    }

    private static string ToErrorMessage(IEnumerable<string> errors)
        => string.Join(" | ", errors);
}

