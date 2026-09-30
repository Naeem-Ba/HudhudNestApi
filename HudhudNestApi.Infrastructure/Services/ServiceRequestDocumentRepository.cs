using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Services.Interfaces;
using HudhudNestApi.Domain.Services.Entities;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Services;

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
