using Microsoft.EntityFrameworkCore;
using PropertyApi.Application.Investments.DTOs;
using PropertyApi.Application.Investments.Interfaces;
using PropertyApi.Application.Properties.DTOs;
using PropertyApi.Domain.Investments.Entities;
using PropertyApi.Domain.Investments.Enums;
using PropertyApi.Domain.Listings.Entities;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Investments;

public sealed class InvestmentProjectRepository : IInvestmentProjectRepository
{
    private readonly AppDbContext _db;

    public InvestmentProjectRepository(AppDbContext db) => _db = db;

    public Task<InvestmentProject?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.InvestmentProjects.FirstOrDefaultAsync(p => p.Id == id, ct);

    public void Add(InvestmentProject project) => _db.InvestmentProjects.Add(project);

    public async Task<PagedResult<InvestmentProjectListDto>> GetPublishedListAsync(
        InvestmentProjectFilterDto filter,
        CancellationToken ct = default)
    {
        var query = Joined().Where(x => x.Project.Status == InvestmentProjectStatus.Published);
        query = ApplyPublicFilters(query, filter);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(x => x.Project.PublishedAt)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(ToListDtoExpression())
            .ToListAsync(ct);

        return new PagedResult<InvestmentProjectListDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = filter.Page,
            PageSize = filter.PageSize,
        };
    }

    public Task<InvestmentProjectDetailsDto?> GetPublishedDetailsAsync(Guid id, CancellationToken ct = default) =>
        Joined()
            .Where(x => x.Project.Id == id && x.Project.Status == InvestmentProjectStatus.Published)
            .Select(ToDetailsDtoExpression())
            .FirstOrDefaultAsync(ct);

    public Task<InvestmentProjectListDto?> GetListItemAsync(Guid id, CancellationToken ct = default) =>
        Joined()
            .Where(x => x.Project.Id == id)
            .Select(ToListDtoExpression())
            .FirstOrDefaultAsync(ct);

    public async Task<PagedResult<AdminInvestmentProjectListDto>> GetAdminListAsync(
        AdminInvestmentProjectFilterDto filter,
        CancellationToken ct = default)
    {
        var query = _db.InvestmentProjects.AsQueryable();

        if (filter.Status.HasValue)
            query = query.Where(p => p.Status == filter.Status.Value);

        if (filter.ProjectType.HasValue)
            query = query.Where(p => p.ProjectType == filter.ProjectType.Value);

        if (!string.IsNullOrWhiteSpace(filter.Search))
            query = query.Where(p => EF.Functions.ILike(p.Title, $"%{filter.Search}%"));

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(p => new AdminInvestmentProjectListDto(
                p.Id, p.Title, p.Status, p.ProjectType, p.OwnerUserId,
                p.TargetAmount, p.RaisedAmount, p.CreatedAt, p.PublishedAt))
            .ToListAsync(ct);

        return new PagedResult<AdminInvestmentProjectListDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = filter.Page,
            PageSize = filter.PageSize,
        };
    }

    public Task<InvestmentProjectDetailsDto?> GetDetailsForAdminAsync(Guid id, CancellationToken ct = default) =>
        Joined()
            .Where(x => x.Project.Id == id)
            .Select(ToDetailsDtoExpression())
            .FirstOrDefaultAsync(ct);

    private IQueryable<(InvestmentProject Project, Property Property)> Joined() =>
        from project in _db.InvestmentProjects.AsNoTracking()
        join property in _db.Properties.AsNoTracking() on project.PropertyId equals property.Id
        select new ValueTuple<InvestmentProject, Property>(project, property);

    private static IQueryable<(InvestmentProject Project, Property Property)> ApplyPublicFilters(
        IQueryable<(InvestmentProject Project, Property Property)> query,
        InvestmentProjectFilterDto filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            query = query.Where(x =>
                EF.Functions.ILike(x.Project.Title, $"%{filter.Search}%") ||
                (x.Project.ShortDescription != null && EF.Functions.ILike(x.Project.ShortDescription, $"%{filter.Search}%")));
        }

        if (filter.ProjectType.HasValue)
            query = query.Where(x => x.Project.ProjectType == filter.ProjectType.Value);

        if (filter.RiskLevel.HasValue)
            query = query.Where(x => x.Project.RiskLevel == filter.RiskLevel.Value);

        if (filter.MinTermMonths.HasValue)
            query = query.Where(x => x.Project.InvestmentTermMonths >= filter.MinTermMonths.Value);

        if (filter.MaxTermMonths.HasValue)
            query = query.Where(x => x.Project.InvestmentTermMonths <= filter.MaxTermMonths.Value);

        if (filter.MinTargetAmount.HasValue)
            query = query.Where(x => x.Project.TargetAmount >= filter.MinTargetAmount.Value);

        if (filter.MaxTargetAmount.HasValue)
            query = query.Where(x => x.Project.TargetAmount <= filter.MaxTargetAmount.Value);

        if (filter.MinExpectedReturn.HasValue)
            query = query.Where(x => x.Project.ExpectedReturnMax >= filter.MinExpectedReturn.Value);

        if (!string.IsNullOrWhiteSpace(filter.City))
            query = query.Where(x => EF.Functions.ILike(x.Property.City, $"%{filter.City}%"));

        return query;
    }

    private static System.Linq.Expressions.Expression<Func<(InvestmentProject Project, Property Property), InvestmentProjectListDto>>
        ToListDtoExpression() => x => new InvestmentProjectListDto(
            x.Project.Id,
            x.Project.Title,
            x.Project.ShortDescription,
            x.Project.ProjectType,
            x.Project.Status,
            x.Property.City,
            x.Property.CountryCode,
            x.Property.Images.Where(i => i.IsMain).Select(i => i.Url).FirstOrDefault(),
            x.Project.TargetAmount,
            x.Project.RaisedAmount,
            x.Project.MinimumInvestment,
            x.Project.MaximumInvestment,
            x.Project.Currency,
            x.Project.InvestmentTermMonths,
            x.Project.ExpectedReturnMin,
            x.Project.ExpectedReturnMax,
            x.Project.RiskLevel,
            x.Project.PublishedAt);

    private static System.Linq.Expressions.Expression<Func<(InvestmentProject Project, Property Property), InvestmentProjectDetailsDto>>
        ToDetailsDtoExpression() => x => new InvestmentProjectDetailsDto(
            x.Project.Id,
            x.Project.PropertyId,
            x.Project.Title,
            x.Project.ShortDescription,
            x.Project.Description,
            x.Project.ProjectType,
            x.Project.Status,
            x.Property.Title,
            x.Property.City,
            x.Property.CountryCode,
            x.Property.Latitude,
            x.Property.Longitude,
            x.Property.Images.Where(i => i.IsMain).Select(i => i.Url).FirstOrDefault(),
            x.Project.TargetAmount,
            x.Project.RaisedAmount,
            x.Project.MinimumInvestment,
            x.Project.MaximumInvestment,
            x.Project.Currency,
            x.Project.InvestmentTermMonths,
            x.Project.ExpectedReturnMin,
            x.Project.ExpectedReturnMax,
            x.Project.RiskLevel,
            x.Project.StartDate,
            x.Project.EndDate,
            x.Project.PublishedAt);
}
