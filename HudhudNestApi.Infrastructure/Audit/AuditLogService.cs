using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Domain.Audit.Entities;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Audit;

public sealed class AuditLogService : IAuditLogService
{
    private const int MaxActionLength = 100;
    private const int MaxIpAddressLength = 64;

    private readonly AppDbContext _db;

    public AuditLogService(AppDbContext db)
    {
        _db = db;
    }

    public async Task LogAsync(
        Guid? userId,
        string action,
        string? ipAddress,
        string? oldValue = null,
        string? newValue = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(action))
            throw new ArgumentException("Audit action is required.", nameof(action));

        var normalizedAction = action.Trim();
        if (normalizedAction.Length > MaxActionLength)
            normalizedAction = normalizedAction[..MaxActionLength];

        var normalizedIp = string.IsNullOrWhiteSpace(ipAddress)
            ? null
            : ipAddress.Trim();

        if (normalizedIp?.Length > MaxIpAddressLength)
            normalizedIp = normalizedIp[..MaxIpAddressLength];

        _db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Action = normalizedAction,
            IpAddress = normalizedIp,
            Timestamp = DateTime.UtcNow,
            OldValue = oldValue,
            NewValue = newValue
        });

        await _db.SaveChangesAsync(ct);
    }
}
