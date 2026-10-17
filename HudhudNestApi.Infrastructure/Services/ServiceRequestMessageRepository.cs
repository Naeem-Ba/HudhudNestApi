using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Services.Interfaces;
using HudhudNestApi.Domain.Services.Entities;
using HudhudNestApi.Domain.Services.Enums;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Services;

public sealed class ServiceRequestMessageRepository : IServiceRequestMessageRepository
{
    private readonly AppDbContext _db;
    public ServiceRequestMessageRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(ServiceRequestMessage message, CancellationToken ct = default)
        => await _db.ServiceRequestMessages.AddAsync(message, ct);

    public async Task<IReadOnlyList<ServiceRequestMessage>> GetThreadAsync(
        Guid serviceRequestId, CancellationToken ct = default)
        => await _db.ServiceRequestMessages
            .AsNoTracking()
            .Where(m => m.ServiceRequestId == serviceRequestId)
            .OrderBy(m => m.CreatedAt)
            .ThenBy(m => m.Id)
            .ToListAsync(ct);

    public async Task<int> CountAsync(Guid serviceRequestId, CancellationToken ct = default)
        => await _db.ServiceRequestMessages.CountAsync(m => m.ServiceRequestId == serviceRequestId, ct);

    public async Task<ServiceRequestMessage?> GetLastAsync(Guid serviceRequestId, CancellationToken ct = default)
        => await _db.ServiceRequestMessages
            .AsNoTracking()
            .Where(m => m.ServiceRequestId == serviceRequestId)
            .OrderByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<ServiceRequestMessage>> GetUnreadForAsync(
        Guid serviceRequestId, ServiceParty reader, CancellationToken ct = default)
        => await _db.ServiceRequestMessages
            .Where(m => m.ServiceRequestId == serviceRequestId
                && m.SenderParty != reader
                && m.ReadAt == null)
            .ToListAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, ServiceRequestMessageStats>> GetStatsAsync(
        IReadOnlyCollection<Guid> serviceRequestIds, ServiceParty viewer, CancellationToken ct = default)
    {
        if (serviceRequestIds.Count == 0)
            return new Dictionary<Guid, ServiceRequestMessageStats>();

        var rows = await _db.ServiceRequestMessages
            .AsNoTracking()
            .Where(m => serviceRequestIds.Contains(m.ServiceRequestId))
            .GroupBy(m => m.ServiceRequestId)
            .Select(g => new
            {
                RequestId = g.Key,
                Total = g.Count(),
                Unread = g.Count(m => m.SenderParty != viewer && m.ReadAt == null),
                LastAt = g.Max(m => m.CreatedAt),
            })
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.RequestId, r => new ServiceRequestMessageStats(r.Total, r.Unread, r.LastAt));
    }
}
