using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Domain.Auth.Entities;
using PropertyApi.Domain.Enums;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Auth.Repositories;

public sealed class OtpCodeRepository : IOtpCodeRepository
{
    private readonly AppDbContext _db;

    public OtpCodeRepository(AppDbContext db)
        => _db = db;

    public async Task AddAsync(OtpCode otpCode, CancellationToken ct = default)
        => await _db.OtpCodes.AddAsync(otpCode, ct);

    public async Task<OtpCode?> GetLatestValidAsync(
        string phoneNumber,
        OtpPurpose purpose,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        await DeleteExpiredAsync(now, ct);

        return await _db.OtpCodes
            .Where(o =>
                o.PhoneNumber == phoneNumber &&
                o.Purpose == purpose &&
                o.ExpiresAt > now &&
                !o.IsUsed &&
                o.AttemptCount < 3)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<int> CountRecentAsync(
        string phoneNumber,
        TimeSpan window,
        CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - window;

        await DeleteExpiredAsync(DateTime.UtcNow, ct);

        return await _db.OtpCodes
            .CountAsync(o =>
                o.PhoneNumber == phoneNumber &&
                o.CreatedAt >= cutoff,
                ct);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _db.SaveChangesAsync(ct);

    public async Task<int> DeleteExpiredAsync(
    DateTime utcNow,
    CancellationToken ct = default)
    {
        return await _db.OtpCodes
            .Where(o => o.ExpiresAt <= utcNow)
            .ExecuteDeleteAsync(ct);
    }

    public async Task<bool> TryConsumeAsync(
    Guid otpCodeId,
    DateTime utcNow,
    CancellationToken ct = default)
    {
        var affectedRows = await _db.OtpCodes
            .Where(otp =>
                otp.Id == otpCodeId &&
                !otp.IsUsed &&
                otp.ExpiresAt > utcNow &&
                otp.AttemptCount < 3)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(otp => otp.IsUsed, true),
                ct);

        return affectedRows == 1;
    }
}