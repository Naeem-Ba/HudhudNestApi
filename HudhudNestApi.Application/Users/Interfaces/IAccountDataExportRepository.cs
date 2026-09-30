using HudhudNestApi.Application.Users.DTOs;

namespace HudhudNestApi.Application.Users.Interfaces;

/// <summary>
/// Finding F7 (docs/ACCOUNT-DELETION-PRODUCTION-READINESS.md). Assembles everything a
/// self-service export returns for exactly one account -- see AccountDataExportDto's own doc
/// comment for what is deliberately excluded.
/// </summary>
public interface IAccountDataExportRepository
{
    Task<AccountDataExportDto> BuildExportAsync(
        Guid userId,
        CancellationToken ct = default);
}
