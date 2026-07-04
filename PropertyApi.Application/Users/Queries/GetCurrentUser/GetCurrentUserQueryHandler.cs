using MediatR;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Auth.Models;
using PropertyApi.Application.Users.DTOs;
using PropertyApi.Application.Users.Interfaces;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Application.Users.Queries.GetCurrentUser;

public sealed class GetCurrentUserQueryHandler
    : IRequestHandler<GetCurrentUserQuery, UserDto?>
{
    private readonly IPureIdentityService _identity;
    private readonly IUserRepository _users;

    public GetCurrentUserQueryHandler(
        IPureIdentityService identity,
        IUserRepository users)
    {
        _identity = identity;
        _users = users;
    }

    public async Task<UserDto?> Handle(
        GetCurrentUserQuery request,
        CancellationToken cancellationToken)
    {
        var user = await _users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null || user.IsDeleted)
            return null;

        var identity = await _identity.FindByIdAsync(request.UserId, cancellationToken);
        if (identity is null)
            return null;

        var roles = await _identity.GetRolesAsync(identity.IdentityId, cancellationToken);
        return MapToDto(user, identity, roles);
    }

    private static UserDto MapToDto(
        User user,
        IdentityAccountSnapshot identity,
        IReadOnlyList<string> roles) => new()
    {
        Id = user.Id,
        Email = identity.Email ?? string.Empty,
        FirstName = user.FirstName,
        LastName = user.LastName,
        DisplayName = user.DisplayName,
        PhoneNumber = identity.PhoneNumber,
        ProfileImageUrl = user.ProfileImageUrl,
        PreferredLanguage = user.PreferredLanguage,
        PreferredCurrency = user.PreferredCurrency,
        CountryCode = user.CountryCode,
        EmailConfirmed = identity.EmailConfirmed,
        CreatedAt = user.CreatedAt,
        Roles = roles.ToList().AsReadOnly()
    };
}

