using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Investments.DTOs;
using HudhudNestApi.Application.Investments.Interfaces;
using HudhudNestApi.Domain.Investments.Entities;
using HudhudNestApi.Domain.Investments.Enums;
using HudhudNestApi.Domain.Users.Constants;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Investments;

public sealed class InvestmentInterestRepository : IInvestmentInterestRepository
{
    private readonly AppDbContext _db;

    public InvestmentInterestRepository(AppDbContext db) => _db = db;

    public Task<InvestmentInterest?> GetAsync(Guid userId, Guid investmentProjectId, CancellationToken ct = default) =>
        _db.InvestmentInterests.FirstOrDefaultAsync(i => i.UserId == userId && i.InvestmentProjectId == investmentProjectId, ct);

    /// <summary>A user may only express interest in a project that actually exists AND is
    /// currently Published — never in a Draft/UnderReview/Closed one (Phase 1 spec §30).</summary>
    public Task<bool> ProjectExistsAndPublishedAsync(Guid investmentProjectId, CancellationToken ct = default) =>
        _db.InvestmentProjects.AnyAsync(p => p.Id == investmentProjectId && p.Status == InvestmentProjectStatus.Published, ct);

    public void Add(InvestmentInterest interest) => _db.InvestmentInterests.Add(interest);

    public async Task<IReadOnlyList<InvestmentInterestDto>> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
    {
        var query =
            from interest in _db.InvestmentInterests.AsNoTracking()
            join project in _db.InvestmentProjects.AsNoTracking() on interest.InvestmentProjectId equals project.Id
            join property in _db.Properties.AsNoTracking() on project.PropertyId equals property.Id
            // P10-D05: same rule as the watchlist — only publicly visible projects are listed.
            where interest.UserId == userId && project.Status == InvestmentProjectStatus.Published
            orderby interest.CreatedAt descending
            select new InvestmentInterestDto(interest.Id, interest.InvestmentProjectId, project.Title, interest.Status, interest.CreatedAt);

        return await query.ToListAsync(ct);
    }

    /// <summary>Staff view. Names are decrypted by EF value converters, so they are read whole and composed here
    /// (same approach as the listing-report repository).</summary>
    public async Task<IReadOnlyList<AdminInvestmentInterestDto>> GetForProjectAsync(
        Guid investmentProjectId, CancellationToken ct = default)
    {
        var rows = await (
            from interest in _db.InvestmentInterests.AsNoTracking()
            join user in _db.Users.AsNoTracking() on interest.UserId equals user.Id into users
            from user in users.DefaultIfEmpty()
            where interest.InvestmentProjectId == investmentProjectId
            orderby interest.CreatedAt descending
            select new
            {
                interest.Id,
                interest.UserId,
                interest.Status,
                interest.CreatedAt,
                interest.UpdatedAt,
                Email = user != null ? user.Email : null,
                Phone = user != null ? user.PhoneNumber : null,
            }).ToListAsync(ct);

        var ids = rows.Select(x => x.UserId).Distinct().ToList();
        var accounts = await _db.UserAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(a => ids.Contains(a.Id))
            .Select(a => new { a.Id, a.DisplayName, a.FirstName, a.LastName })
            .ToListAsync(ct);
        var names = accounts.ToDictionary(
            a => a.Id,
            a => !string.IsNullOrWhiteSpace(a.DisplayName)
                ? a.DisplayName!
                : $"{a.FirstName} {a.LastName}".Trim() is { Length: > 0 } full ? full : "-");

        return rows
            .Select(x => new AdminInvestmentInterestDto(
                x.Id, x.UserId, names.TryGetValue(x.UserId, out var name) ? name : "-",
                x.Email, x.Phone, x.Status, x.CreatedAt, x.UpdatedAt))
            .ToList();
    }

    public Task<int> CountActiveAsync(Guid investmentProjectId, CancellationToken ct = default) =>
        _db.InvestmentInterests.CountAsync(
            i => i.InvestmentProjectId == investmentProjectId && i.Status == InvestmentInterestStatus.Active, ct);

    public async Task<IReadOnlyList<Guid>> ListAdminIdsAsync(CancellationToken ct = default) =>
        await (from user in _db.Users.AsNoTracking()
               join userRole in _db.UserRoles.AsNoTracking() on user.Id equals userRole.UserId
               join role in _db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
               join account in _db.UserAccounts.AsNoTracking() on user.Id equals account.Id
               where role.Name == RoleNames.Admin && !account.IsDeleted
               select user.Id).ToListAsync(ct);
}
