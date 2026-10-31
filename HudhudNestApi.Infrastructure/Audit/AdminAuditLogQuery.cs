using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Admin.Interfaces;
using HudhudNestApi.Application.Common.Paging;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Audit;

public sealed class AdminAuditLogQuery : IAdminAuditLogQuery
{
    public const int MaxPageSize = 100;
    private const int MaxValueLength = 4000;

    private readonly AppDbContext _db;

    public AdminAuditLogQuery(AppDbContext db) => _db = db;

    public async Task<AdminAuditLogPage> SearchAsync(AdminAuditLogFilter filter, CancellationToken ct = default)
    {
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, MaxPageSize);

        var query = _db.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Action))
        {
            var action = filter.Action.Trim();
            query = query.Where(a => a.Action == action);
        }

        if (filter.UserId is { } userId)
            query = query.Where(a => a.UserId == userId);

        if (filter.FromUtc is { } from)
            query = query.Where(a => a.Timestamp >= from);

        if (filter.ToUtc is { } to)
            query = query.Where(a => a.Timestamp <= to);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            // LIKE wildcards in the term are escaped so "%" searches for a percent sign, not everything.
            var term = filter.Search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            var like = $"%{term}%";
            query = query.Where(a =>
                EF.Functions.ILike(a.Action, like)
                || EF.Functions.ILike(a.NewValue ?? string.Empty, like)
                || EF.Functions.ILike(a.OldValue ?? string.Empty, like));
        }

        var total = await query.CountAsync(ct);

        var rows = await query
            .OrderByDescending(a => a.Timestamp)
            .ThenByDescending(a => a.Id)
            .SkipPage(page, pageSize)
            .Take(pageSize)
            .Select(a => new
            {
                a.Id,
                a.Timestamp,
                a.UserId,
                a.Action,
                a.IpAddress,
                a.OldValue,
                a.NewValue,
            })
            .ToListAsync(ct);

        var userIds = rows.Where(r => r.UserId.HasValue).Select(r => r.UserId!.Value).Distinct().ToList();
        var names = userIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.UserAccounts.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.FirstName, u.LastName })
                .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), ct);

        static string? Cut(string? value)
            => value is { Length: > MaxValueLength } ? value[..MaxValueLength] : value;

        var items = rows.Select(r => new AdminAuditLogEntryDto(
            r.Id,
            r.Timestamp,
            r.UserId,
            r.UserId is { } id ? names.GetValueOrDefault(id) : null,
            r.Action,
            r.IpAddress,
            Cut(r.OldValue),
            Cut(r.NewValue))).ToList();

        return new AdminAuditLogPage(items, total, page, pageSize);
    }
}
