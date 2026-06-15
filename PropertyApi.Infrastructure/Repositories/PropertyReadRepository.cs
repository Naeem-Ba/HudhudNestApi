using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Repositories;

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
                    : null))
            .FirstOrDefaultAsync(ct);
    }
}