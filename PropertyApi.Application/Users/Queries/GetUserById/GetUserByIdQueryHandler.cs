using MediatR;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Auth.Models;
using PropertyApi.Application.Users.DTOs;
using PropertyApi.Application.Users.Interfaces;
using PropertyApi.Domain.Users.Constants;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Application.Users.Queries.GetUserById;

public sealed class GetUserByIdQueryHandler
    : IRequestHandler<GetUserByIdQuery, UserSummaryDto?>
{
    private readonly IUserIdentityReadService _identity;
    private readonly IUserAccountRepository _accounts;

    public GetUserByIdQueryHandler(
        IUserIdentityReadService identity,
        IUserAccountRepository accounts)
    {
        _identity = identity;
        _accounts = accounts;
    }

    public async Task<UserSummaryDto?> Handle(
        GetUserByIdQuery request,
        CancellationToken cancellationToken)
    {
        var identity =
            await _identity.FindByIdAsync(
                request.UserId,
                cancellationToken);

        if (identity is null ||
            identity.IsDeleted)
        {
            return null;
        }

        var account =
            await _accounts.GetByIdAsync(
                identity.UserAccountId,
                cancellationToken);

        var roles =
            await _identity.GetRolesAsync(
                identity.IdentityId,
                cancellationToken);

        return MapToSummaryDto(
            identity,
            account,
            roles);
    }

    private static UserSummaryDto MapToSummaryDto(
        IdentityAccountSnapshot identity,
        UserAccount? account,
        IReadOnlyList<string> roles)
    {
        var isAgent =
            roles.Any(
                role =>
                    string.Equals(
                        role,
                        RoleNames.Agent,
                        StringComparison.OrdinalIgnoreCase));

        return new UserSummaryDto
        {
            Id =
                identity.UserAccountId,

            DisplayName =
                BuildDisplayName(
                    account),

            ProfileImageUrl =
                account?.ProfileImageUrl,

            PhoneNumber =
                isAgent
                    ? identity.PhoneNumber
                    : null,

            Roles =
                roles
                    .ToList()
                    .AsReadOnly()
        };
    }

    private static string BuildDisplayName(
        UserAccount? account)
    {
        if (account is null)
        {
            return "User";
        }

        if (!string.IsNullOrWhiteSpace(
                account.DisplayName))
        {
            return account.DisplayName;
        }

        var fullName =
            $"{account.FirstName} {account.LastName}"
                .Trim();

        return string.IsNullOrWhiteSpace(
            fullName)
                ? "User"
                : fullName;
    }
}
