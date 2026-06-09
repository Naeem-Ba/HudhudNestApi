using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Domain.Auth.Entities;
using PropertyApi.Domain.Auth.Enums;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Auth.Repositories;

/// <summary>
/// مستودع OtpCode — يتعامل مع قاعدة البيانات
///
/// ملاحظة للمبتدئين:
/// ──────────────────
/// Repository Pattern = طبقة وسيطة بين الكود والقاعدة.
/// يمنع تشتت استعلامات قاعدة البيانات في كل مكان.
/// كل الاستعلامات المتعلقة بـ OtpCode هنا فقط.
/// </summary>
public sealed class OtpCodeRepository : IOtpCodeRepository
{
    private readonly AppDbContext _db;

    public OtpCodeRepository(AppDbContext db)
        => _db = db;

    /// <summary>أضف رمز OTP جديداً للقاعدة</summary>
    public async Task AddAsync(OtpCode otpCode, CancellationToken ct = default)
        => await _db.OtpCodes.AddAsync(otpCode, ct);

    /// <summary>
    /// احصل على آخر رمز صالح
    ///
    /// "صالح" يعني:
    ///   • رقم الهاتف متطابق
    ///   • الغرض متطابق (تسجيل / دخول / ...)
    ///   • لم ينتهِ بعد (ExpiresAt > الآن)
    ///   • لم يُستخدم (IsUsed = false)
    ///   • عدد المحاولات < 3 (AttemptCount < 3)
    ///   • نأخذ الأحدث فقط (CreatedAt الأكبر)
    /// </summary>
    public async Task<OtpCode?> GetLatestValidAsync(
        string phoneNumber,
        OtpPurpose purpose,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

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

    /// <summary>
    /// عُدّ طلبات OTP الأخيرة لرقم الهاتف
    ///
    /// يُستخدم لفحص حد الطلبات:
    ///   إذا طلب المستخدم 3+ رموز في ساعة → ارفض
    /// </summary>
    public async Task<int> CountRecentAsync(
        string phoneNumber,
        TimeSpan window,
        CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - window;

        return await _db.OtpCodes
            .CountAsync(o =>
                o.PhoneNumber == phoneNumber &&
                o.CreatedAt >= cutoff,
                ct);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _db.SaveChangesAsync(ct);
}