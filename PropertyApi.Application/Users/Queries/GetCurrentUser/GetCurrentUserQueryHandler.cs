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
    private readonly IUserIdentityReadService _identity;
    private readonly IUserAccountRepository _accounts;

    public GetCurrentUserQueryHandler(
        IUserIdentityReadService identity,
        IUserAccountRepository accounts)
    {
        _identity = identity;
        _accounts = accounts;
    }

    public async Task<UserDto?> Handle(
        GetCurrentUserQuery request,
        CancellationToken cancellationToken)
    {
        var identity = await _identity.FindByIdAsync(
            request.UserId,
            cancellationToken);

        if (identity is null || identity.IsDeleted)
        {
            return null;
        }

        var account = await _accounts.GetByIdAsync(
            identity.UserAccountId,
            cancellationToken);

        if (account is null)
        {
            return null;
        }

        var roles = await _identity.GetRolesAsync(
            identity.IdentityId,
            cancellationToken);

        return MapToDto(account, identity, roles);
    }

    private static UserDto MapToDto(
        UserAccount account,
        IdentityAccountSnapshot identity,
        IReadOnlyList<string> roles)
    {
        return new UserDto
        {
            Id = account.Id,
            Email = identity.Email ?? string.Empty,
            FirstName = account.FirstName,
            LastName = account.LastName,
            DisplayName = account.DisplayName,
            PhoneNumber = identity.PhoneNumber,
            ProfileImageUrl = account.ProfileImageUrl,
            PreferredLanguage = account.PreferredLanguage,
            PreferredCurrency = account.PreferredCurrency,
            CountryCode = account.CountryCode,
            EmailConfirmed = identity.EmailConfirmed,
            CreatedAt = account.CreatedAt,
            Roles = roles.ToList().AsReadOnly()
        };
    }
}
