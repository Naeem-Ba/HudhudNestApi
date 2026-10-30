using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Services.Interfaces;
using HudhudNestApi.Domain.Services.Entities;
using HudhudNestApi.Domain.Services.Enums;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Application.Common.Paging;

namespace HudhudNestApi.Infrastructure.Services;

public sealed class ServiceProviderApplicationRepository : IServiceProviderApplicationRepository
{
    private readonly AppDbContext _db;
    public ServiceProviderApplicationRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(ServiceProviderApplication application, CancellationToken ct = default)
        => await _db.ServiceProviderApplications.AddAsync(application, ct);

    public async Task<ServiceProviderApplication?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.ServiceProviderApplications.FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task<ServiceProviderApplication?> GetOpenByApplicantAsync(Guid applicantUserId, CancellationToken ct = default)
        => await _db.ServiceProviderApplications.FirstOrDefaultAsync(a =>
            a.ApplicantUserId == applicantUserId &&
            (a.Status == ServiceProviderApplicationStatus.Pending || a.Status == ServiceProviderApplicationStatus.NeedsInfo), ct);

    public async Task<IReadOnlyList<ServiceProviderApplication>> ListByApplicantAsync(
        Guid applicantUserId, CancellationToken ct = default)
        => await _db.ServiceProviderApplications
            .AsNoTracking()
            .Where(a => a.ApplicantUserId == applicantUserId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(ct);

    public async Task<(IReadOnlyList<ServiceProviderApplication> Items, int Total)> SearchAsync(
        ServiceProviderApplicationFilter filter, CancellationToken ct = default)
    {
        var query = _db.ServiceProviderApplications.AsNoTracking();

        if (filter.Status is { } status)
            query = query.Where(a => a.Status == status);

        if (filter.Category is { } category)
        {
            var name = category.ToString();
            query = query.Where(a => a.RequestedCategories.Contains(name));
        }

        if (filter.GovernorateId is { } governorateId)
            query = query.Where(a => a.RequestedGovernorateIds.Contains(governorateId));

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            var pattern = $"%{EscapeLike(term)}%";
            var number = TryParseReference(term);
            query = query.Where(a =>
                (number != null && a.Number == number) ||
                EF.Functions.ILike(a.DisplayName, pattern, "\\") ||
                EF.Functions.ILike(a.ContactName, pattern, "\\") ||
                (a.ContactEmail != null && EF.Functions.ILike(a.ContactEmail, pattern, "\\")) ||
                (a.ContactPhone != null && EF.Functions.ILike(a.ContactPhone, pattern, "\\")));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .SkipPage(filter.Page, filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<int> CountPendingAsync(CancellationToken ct = default)
        => await _db.ServiceProviderApplications.CountAsync(a => a.Status == ServiceProviderApplicationStatus.Pending, ct);

    /// <summary>"SPA-000042", "spa-42" or "42" → 42.</summary>
    private static long? TryParseReference(string term)
    {
        var digits = term.StartsWith("SPA-", StringComparison.OrdinalIgnoreCase) ? term[4..] : term;
        return long.TryParse(digits, out var n) && n > 0 ? n : null;
    }

    internal static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
