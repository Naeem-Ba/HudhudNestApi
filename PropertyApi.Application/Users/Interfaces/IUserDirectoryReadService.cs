using PropertyApi.Application.Users.DTOs;

namespace PropertyApi.Application.Users.Interfaces;

/// <summary>
/// Read boundary for user-directory queries.
///
/// Combines Identity state, business-profile state,
/// and authorization roles without exposing Identity
/// entities to the Application layer.
/// </summary>
public interface IUserDirectoryReadService
{
    Task<IReadOnlyList<UserSummaryDto>>
        GetAllActiveAsync(
            CancellationToken ct = default);
}