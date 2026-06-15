using MediatR;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Users.DTOs;
using PropertyApi.Domain.Users.Constants;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Application.Users.Queries.GetUserById;

public sealed class GetUserByIdQueryHandler
    : IRequestHandler<GetUserByIdQuery, UserSummaryDto?>
{
    private readonly IIdentityUserService _identityUsers;

    public GetUserByIdQueryHandler(IIdentityUserService identityUsers)
        => _identityUsers = identityUsers;

    public async Task<UserSummaryDto?> Handle(
        GetUserByIdQuery request,
        CancellationToken cancellationToken)
    {
        var user = await _identityUsers.FindByIdAsync(request.UserId, cancellationToken);
        if (user is null || user.IsDeleted)
            return null;

        var roles = await _identityUsers.GetRolesAsync(user, cancellationToken);
        return MapToSummaryDto(user, roles);
    }

    private static UserSummaryDto MapToSummaryDto(User user, IReadOnlyList<string> roles)
    {
        var isAgent = roles.Any(role =>
            string.Equals(role, RoleNames.Agent, StringComparison.OrdinalIgnoreCase));

        return new UserSummaryDto
        {
            Id = user.Id,
            DisplayName = BuildDisplayName(user),
            ProfileImageUrl = user.ProfileImageUrl,
            PhoneNumber = isAgent ? user.PhoneNumber : null,
            Roles = roles.ToList().AsReadOnly()
        };
    }

    private static string BuildDisplayName(User user)
    {
        if (!string.IsNullOrWhiteSpace(user.DisplayName))
            return user.DisplayName;

        var fullName = $"{user.FirstName} {user.LastName}".Trim();
        return string.IsNullOrWhiteSpace(fullName) ? "User" : fullName;
    }
}

