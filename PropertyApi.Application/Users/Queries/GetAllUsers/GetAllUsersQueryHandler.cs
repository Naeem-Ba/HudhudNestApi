using MediatR;
using PropertyApi.Application.Users.DTOs;
using PropertyApi.Application.Users.Interfaces;

namespace PropertyApi.Application.Users.Queries.GetAllUsers;

public sealed class GetAllUsersQueryHandler
    : IRequestHandler<
        GetAllUsersQuery,
        IReadOnlyList<UserSummaryDto>>
{
    private readonly IUserDirectoryReadService _directory;

    public GetAllUsersQueryHandler(
        IUserDirectoryReadService directory)
    {
        _directory = directory;
    }

    public Task<IReadOnlyList<UserSummaryDto>> Handle(
        GetAllUsersQuery request,
        CancellationToken cancellationToken)
        => _directory.GetAllActiveAsync(
            cancellationToken);
}