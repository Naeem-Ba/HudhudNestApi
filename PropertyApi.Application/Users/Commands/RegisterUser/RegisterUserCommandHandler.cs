using MediatR;
using Microsoft.AspNetCore.Identity;
using PropertyApi.Application.Users.DTOs;
using PropertyApi.Domain.Users.Entities;
using PropertyApi.Domain.Users.Constants;

namespace PropertyApi.Application.Users.Commands.RegisterUser;

public sealed class RegisterUserCommandHandler
    : IRequestHandler<RegisterUserCommand, UserDto>
{
    private const string DefaultRole = RoleNames.User;

    private readonly UserManager<User> _userManager;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;

    public RegisterUserCommandHandler(
        UserManager<User> userManager,
        RoleManager<IdentityRole<Guid>> roleManager)
    {
        _userManager = userManager;
        _roleManager = roleManager;
    }

    public async Task<UserDto> Handle(
        RegisterUserCommand request,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        var existingUser = await _userManager.FindByEmailAsync(email);
        if (existingUser is not null)
            throw new InvalidOperationException("Email already registered.");

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
            IsAgent = false,
            PreferredLanguage = request.PreferredLanguage.Trim().ToLowerInvariant(),
            PreferredCurrency = request.PreferredCurrency.Trim().ToUpperInvariant(),
            CountryCode = string.IsNullOrWhiteSpace(request.CountryCode)
                ? null
                : request.CountryCode.Trim().ToUpperInvariant(),
            CreatedAt = now,
            UpdatedAt = now
        };

        var createResult = await _userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
            throw new InvalidOperationException(ToErrorMessage(createResult));

        if (!await _roleManager.RoleExistsAsync(DefaultRole))
            await _roleManager.CreateAsync(new IdentityRole<Guid>(DefaultRole));

        var roleResult = await _userManager.AddToRoleAsync(user, DefaultRole);
        if (!roleResult.Succeeded)
            throw new InvalidOperationException(ToErrorMessage(roleResult));

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
            Roles = new[] { DefaultRole }
        };
    }

    private static string ToErrorMessage(IdentityResult result)
        => string.Join(" | ", result.Errors.Select(e => e.Description));
}
