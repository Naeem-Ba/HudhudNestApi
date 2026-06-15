using MediatR;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Users.DTOs;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Application.Users.Queries.GetCurrentUser;

public sealed class GetCurrentUserQueryHandler
    : IRequestHandler<GetCurrentUserQuery, UserDto?>
{
    private readonly IIdentityUserService _identityUsers;

    public GetCurrentUserQueryHandler(IIdentityUserService identityUsers)
        => _identityUsers = identityUsers;

    public async Task<UserDto?> Handle(
        GetCurrentUserQuery request,
        CancellationToken cancellationToken)
    {
        var user = await _identityUsers.FindByIdAsync(request.UserId, cancellationToken);
        if (user is null || user.IsDeleted)
            return null;

        var roles = await _identityUsers.GetRolesAsync(user, cancellationToken);
        return MapToDto(user, roles);
    }

    private static UserDto MapToDto(User user, IReadOnlyList<string> roles) => new()
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

