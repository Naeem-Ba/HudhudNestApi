using Microsoft.EntityFrameworkCore;
using PropertyApi.Application.Marketing.DTOs;
using PropertyApi.Application.Marketing.Interfaces;
using PropertyApi.Domain.Marketing.Entities;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Repositories;

public sealed class LeadRepository : ILeadRepository
{
    private readonly AppDbContext _db;

    public LeadRepository(AppDbContext db)
        => _db = db;

    public void Add(Lead lead)
    {
        _db.Leads.Add(lead);
    }

    public Task<Lead?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return _db.Leads.FindAsync([id], ct).AsTask();
    }

    public async Task<LeadsPageDto> GetPageAsync(
        int page,
        int pageSize,
        string? source,
        string? userType,
        CancellationToken ct = default)
    {
        var query = _db.Leads.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(source))
            query = query.Where(l => l.Source == source);

        if (!string.IsNullOrWhiteSpace(userType))
            query = query.Where(l => l.UserType == userType);

        query = query.OrderByDescending(l => l.CreatedAt);

        var total = await query.CountAsync(ct);

        var data = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new LeadDto(
                l.Id,
                l.FullName,
                l.Phone,
                l.City,
                l.UserType,
                l.Notes,
                l.Source,
                l.Campaign,
                l.OfferId,
                l.OfferId == null
                    ? null
                    : _db.Offers.Where(o => o.Id == l.OfferId).Select(o => o.Name).FirstOrDefault(),
                l.Status.ToString(),
                l.CreatedAt))
            .ToListAsync(ct);

        return new LeadsPageDto(total, page, pageSize, data);
    }
}
