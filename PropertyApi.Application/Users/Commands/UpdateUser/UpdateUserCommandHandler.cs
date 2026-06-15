using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Users.DTOs;
using PropertyApi.Domain.Users.Constants;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Application.Users.Commands.UpdateUser;

public sealed class UpdateUserCommandHandler
    : IRequestHandler<UpdateUserCommand, UserDto?>
{
    private readonly IIdentityUserService _identityUsers;
    private readonly ILogger<UpdateUserCommandHandler> _logger;

    public UpdateUserCommandHandler(
        IIdentityUserService identityUsers,
        ILogger<UpdateUserCommandHandler> logger)
    {
        _identityUsers = identityUsers;
        _logger = logger;
    }

    public async Task<UserDto?> Handle(
        UpdateUserCommand request,
        CancellationToken cancellationToken)
    {
        var user = await _identityUsers.FindByIdAsync(request.UserId, cancellationToken);
        if (user is null || user.IsDeleted)
            return null;

        var roles = await _identityUsers.GetRolesAsync(user, cancellationToken);
        var isAgent = roles.Any(role => string.Equals(role, RoleNames.Agent, StringComparison.OrdinalIgnoreCase));

        if (request.FirstName is not null)
            user.FirstName = request.FirstName.Trim();

        if (request.LastName is not null)
            user.LastName = request.LastName.Trim();

        if (request.DisplayName is not null)
            user.DisplayName = string.IsNullOrWhiteSpace(request.DisplayName)
                ? null
                : request.DisplayName.Trim();

        if (request.PhoneNumber is not null)
            user.PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber)
                ? null
                : request.PhoneNumber.Trim();

        if (request.ProfileImageUrl is not null)
            user.ProfileImageUrl = string.IsNullOrWhiteSpace(request.ProfileImageUrl)
                ? null
                : request.ProfileImageUrl.Trim();

        if (request.PreferredLanguage is not null)
            user.PreferredLanguage = request.PreferredLanguage.Trim().ToLowerInvariant();

        if (request.PreferredCurrency is not null)
            user.PreferredCurrency = request.PreferredCurrency.Trim().ToUpperInvariant();

        if (request.CountryCode is not null)
            user.CountryCode = string.IsNullOrWhiteSpace(request.CountryCode)
                ? null
                : request.CountryCode.Trim().ToUpperInvariant();

        if (isAgent && string.IsNullOrWhiteSpace(user.PhoneNumber))
            throw new InvalidOperationException("PhoneNumber is required for agents.");

        user.UpdatedAt = DateTime.UtcNow;

        var result = await _identityUsers.UpdateAsync(user, cancellationToken);
        if (!result.Succeeded)
        {
            _logger.LogWarning(
                "User profile update failed for user {UserId}. Errors: {Errors}",
                user.Id,
                string.Join(", ", result.Errors));

            throw new InvalidOperationException(
                string.Join(" | ", result.Errors));
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
            Roles = roles.ToList().AsReadOnly()
        };
    }
}

