namespace HudhudNestApi.Application.Admin.Interfaces;

/// <summary>Read-only view of the audit log for administrators (phase 14, D06).</summary>
public interface IAdminAuditLogQuery
{
    Task<AdminAuditLogPage> SearchAsync(AdminAuditLogFilter filter, CancellationToken ct = default);
}

public sealed record AdminAuditLogFilter(
    int Page = 1,
    int PageSize = 50,
    string? Action = null,
    Guid? UserId = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    string? Search = null);

public sealed record AdminAuditLogEntryDto(
    Guid Id,
    DateTime Timestamp,
    Guid? UserId,
    string? UserName,
    string Action,
    string? IpAddress,
    string? OldValue,
    string? NewValue);

public sealed record AdminAuditLogPage(
    IReadOnlyList<AdminAuditLogEntryDto> Items,
    int Total,
    int Page,
    int PageSize);
