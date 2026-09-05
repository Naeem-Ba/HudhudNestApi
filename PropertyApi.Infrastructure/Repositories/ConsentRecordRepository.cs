using Microsoft.EntityFrameworkCore;
using PropertyApi.Application.Users.Interfaces;
using PropertyApi.Domain.Enums;
using PropertyApi.Domain.Users.Entities;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Repositories;

public sealed class ConsentRecordRepository : IConsentRecordRepository
{
    private readonly AppDbContext _db;

    public ConsentRecordRepository(AppDbContext db)
        => _db = db;

    public void Add(ConsentRecord record)
    {
        _db.ConsentRecords.Add(record);
    }

    public async Task<IReadOnlyList<ConsentRecord>> GetByUserIdAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        return await _db.ConsentRecords
            .Where(record => record.UserId == userId)
            .OrderByDescending(record => record.ConsentedAtUtc)
            .ToListAsync(ct);
    }

    public Task<ConsentRecord?> GetActiveAsync(
        Guid userId,
        ConsentPolicyType policyType,
        string policyVersion,
        CancellationToken ct = default)
    {
        return _db.ConsentRecords
            .FirstOrDefaultAsync(
                record =>
                    record.UserId == userId &&
                    record.PolicyType == policyType &&
                    record.PolicyVersion == policyVersion &&
                    record.WithdrawnAtUtc == null,
                ct);
    }

    public async Task<IReadOnlyList<ConsentRecord>> GetActiveByTypeAsync(
        Guid userId,
        ConsentPolicyType policyType,
        CancellationToken ct = default)
    {
        return await _db.ConsentRecords
            .Where(record =>
                record.UserId == userId &&
                record.PolicyType == policyType &&
                record.WithdrawnAtUtc == null)
            .ToListAsync(ct);
    }
}
