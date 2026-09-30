using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Repositories;

public sealed class PropertyReadRepository : IPropertyReadRepository
{
    private readonly AppDbContext _db;
    public PropertyReadRepository(AppDbContext db) => _db = db;

    public async Task<PropertySummary?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.Properties
            .Where(p => p.Id == id)
            .Select(p => new PropertySummary(
                p.Id,
                p.Title,
                p.City,
                p.OwnerId,
                p.IsPublished,
                p.Images.FirstOrDefault(i => i.IsMain) != null
                    ? p.Images.First(i => i.IsMain).Url
                    : null,
                p.CountryCode,
                p.Latitude,
                p.Longitude,
                p.PurchasePrice ?? p.ColdRent,
                p.CurrencyCode))
            .FirstOrDefaultAsync(ct);
    }
}