using Microsoft.EntityFrameworkCore;
using PropertyApi.Application.Services.Interfaces;
using PropertyApi.Domain.Services.Entities;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Services;

public sealed class ServiceRequestDocumentRepository : IServiceRequestDocumentRepository
{
    private readonly AppDbContext _db;
    public ServiceRequestDocumentRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(ServiceRequestDocument document, CancellationToken ct = default)
        => await _db.ServiceRequestDocuments.AddAsync(document, ct);

    public async Task<IReadOnlyList<ServiceRequestDocument>> GetByServiceRequestIdAsync(
        Guid serviceRequestId, CancellationToken ct = default)
        => await _db.ServiceRequestDocuments
            .Where(d => d.ServiceRequestId == serviceRequestId)
            .OrderBy(d => d.CreatedAt)
            .ToListAsync(ct);
}
