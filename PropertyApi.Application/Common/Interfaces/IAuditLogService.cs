namespace PropertyApi.Application.Common.Interfaces;

public interface IAuditLogService
{
    Task LogAsync(
        Guid? userId,
        string action,
        string? ipAddress,
        string? oldValue = null,
        string? newValue = null,
        CancellationToken ct = default);
}
