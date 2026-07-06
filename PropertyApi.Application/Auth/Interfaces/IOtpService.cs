using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PropertyApi.Domain.Auth.Entities;
using PropertyApi.Domain.Enums;

namespace PropertyApi.Application.Auth.Interfaces;


public interface IOtpService
{

    (string otp, string hash) Generate();

    bool Verify(string otp, string storedHash);
}

public interface ISmsService
{

    Task<bool> SendOtpAsync(
        string phoneNumber,
        string otp,
        CancellationToken ct = default);
}

public interface IOtpCodeRepository
{
    Task AddAsync(OtpCode otpCode, CancellationToken ct = default);

    Task<int> DeleteExpiredAsync(
    DateTime utcNow,
    CancellationToken ct = default);

    Task<OtpCode?> GetLatestValidAsync(
        string phoneNumber,
        OtpPurpose purpose,
        CancellationToken ct = default);

    Task<int> CountRecentAsync(
        string phoneNumber,
        TimeSpan window,
        CancellationToken ct = default);

    Task<bool> TryConsumeAsync(
    Guid otpCodeId,
    DateTime utcNow,
    CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}

public interface IEmailVerificationService
{
    Task SendVerificationLinkAsync(
        string email,
        string verificationToken,
        CancellationToken ct = default);
}