using MediatR;
using Microsoft.AspNetCore.Identity;
using PropertyApi.Application.Users.DTOs;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Application.Users.Commands.UpdateUser;

public sealed class UpdateUserCommandHandler
    : IRequestHandler<UpdateUserCommand, UserDto?>
{
    private readonly UserManager<User> _userManager;

    public UpdateUserCommandHandler(UserManager<User> userManager)
        => _userManager = userManager;

    public async Task<UserDto?> Handle(
        UpdateUserCommand request,
        CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(request.UserId.ToString());
        if (user is null || user.IsDeleted)
            return null;

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

        if (request.IsAgent.HasValue)
            user.IsAgent = request.IsAgent.Value;

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

        if (user.IsAgent && string.IsNullOrWhiteSpace(user.PhoneNumber))
            throw new InvalidOperationException("PhoneNumber is required for agents.");

        user.UpdatedAt = DateTime.UtcNow;

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                string.Join(" | ", result.Errors.Select(e => e.Description)));

        var roles = await _userManager.GetRolesAsync(user);

        return new UserDto
        {
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            FirstName = user.FirstName,
            LastName = user.LastName,
            DisplayName = user.DisplayName,
            PhoneNumber = user.PhoneNumber,
            IsAgent = user.IsAgent,
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
