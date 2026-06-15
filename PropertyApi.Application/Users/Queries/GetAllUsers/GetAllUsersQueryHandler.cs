using MediatR;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Users.DTOs;
using PropertyApi.Application.Users.Interfaces;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Application.Users.Queries.GetAllUsers;

public sealed class GetAllUsersQueryHandler
    : IRequestHandler<GetAllUsersQuery, IReadOnlyList<UserSummaryDto>>
{
    private readonly IUserRepository _users;
    private readonly IIdentityUserService _identityUsers;

    public GetAllUsersQueryHandler(
        IUserRepository users,
        IIdentityUserService identityUsers)
    {
        _users = users;
        _identityUsers = identityUsers;
    }

    public async Task<IReadOnlyList<UserSummaryDto>> Handle(
        GetAllUsersQuery request,
        CancellationToken cancellationToken)
    {
        var users = await _users.GetAllActiveAsync(cancellationToken);
        var result = new List<UserSummaryDto>(users.Count);

        foreach (var user in users)
        {
            var roles = await _identityUsers.GetRolesAsync(user, cancellationToken);
            result.Add(new UserSummaryDto
            {
                Id = user.Id,
                DisplayName = BuildDisplayName(user),
                ProfileImageUrl = user.ProfileImageUrl,
                PhoneNumber = user.PhoneNumber,
                Roles = roles.ToList().AsReadOnly()
            });
        }

        return result.AsReadOnly();
    }

    private static string BuildDisplayName(User user)
    {
        if (!string.IsNullOrWhiteSpace(user.DisplayName))
            return user.DisplayName;

        var fullName = $"{user.FirstName} {user.LastName}".Trim();
        return string.IsNullOrWhiteSpace(fullName) ? "User" : fullName;
    }
}

