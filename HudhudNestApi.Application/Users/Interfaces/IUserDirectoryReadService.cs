using HudhudNestApi.Application.Users.DTOs;
using HudhudNestApi.Application.Users.Models;

namespace HudhudNestApi.Application.Users.Interfaces;

/// <summary>
/// Read boundary for user-directory queries.
///
/// Combines Identity state and profile state without exposing
/// ASP.NET Identity or persistence entities to the Application layer.
/// </summary>
public interface IUserDirectoryReadService
{
    Task<IReadOnlyList<UserSummaryDto>>
        GetAllActiveAsync(
            CancellationToken ct = default);

    Task<UserDirectoryEntry?>
        GetActiveByIdAsync(
            Guid userId,
            CancellationToken ct = default);
}