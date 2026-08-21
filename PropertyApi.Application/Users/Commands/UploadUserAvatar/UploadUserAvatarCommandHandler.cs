using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Users.DTOs;
using PropertyApi.Application.Users.Interfaces;

namespace PropertyApi.Application.Users.Commands.UploadUserAvatar;

/// <summary>
/// يرفع صورة حساب المستخدم عبر IMediaStorageService (Cloudinary حاليًا) ويحدّث
/// UserAccount.ProfileImageUrl/ProfileImagePublicId. نفس منهجية التحقق
/// (نوع/حجم/توقيع الملف) وتنظيف التخزين عند الفشل المُطبَّقة فعلاً في
/// UploadPropertyImagesCommandHandler — أبقينا الحدود (2 ميجابايت) مطابقة
/// تمامًا لحد الواجهة الأمامية (profile.component.ts: maxFileSizeMB) كي لا
/// يرى المستخدم رسالتين مختلفتين لنفس القيد.
/// </summary>
public sealed class UploadUserAvatarCommandHandler
    : IRequestHandler<UploadUserAvatarCommand, UploadUserAvatarResult>
{
    private const long MaxAvatarSize = 2_000_000; // 2 MB — مطابق لـ profile.component.ts
    private const string AvatarFolder = "user-avatars";

    private static readonly HashSet<string> AllowedImageTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg",
            "image/jpg", // ليس MIME قياسيًا، لكن الواجهة (profile.component.ts: allowedTypes) تقبله فعليًا
            "image/png"
        };

    private static readonly HashSet<string> AllowedImageExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg",
            ".jpeg",
            ".png"
        };

    private readonly IUserAccountRepository _accounts;
    private readonly IMediaStorageService _storage;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<UploadUserAvatarCommandHandler> _logger;

    public UploadUserAvatarCommandHandler(
        IUserAccountRepository accounts,
        IMediaStorageService storage,
        IUnitOfWork uow,
        ILogger<UploadUserAvatarCommandHandler> logger)
    {
        _accounts = accounts;
        _storage = storage;
        _uow = uow;
        _logger = logger;
    }

    public async Task<UploadUserAvatarResult> Handle(
        UploadUserAvatarCommand request,
        CancellationToken ct)
    {
        var account = await _accounts.GetByIdAsync(request.UserId, ct);
        if (account is null)
        {
            return UploadUserAvatarResult.NotFound("الحساب غير موجود.");
        }

        var validationMessage = await ValidateAvatarFileAsync(request.File, ct);
        if (validationMessage is not null)
        {
            return UploadUserAvatarResult.ValidationFailed(validationMessage);
        }

        if (request.File.Content.CanSeek)
        {
            request.File.Content.Position = 0;
        }

        await using var content = request.File.Content;

        var uploadResult = await _storage.UploadImageAsync(
            content,
            request.File.FileName,
            request.File.ContentType,
            AvatarFolder,
            ct);

        if (!uploadResult.Succeeded)
        {
            return UploadUserAvatarResult.StorageFailed(uploadResult.ErrorMessage);
        }

        var previousPublicId = account.ProfileImagePublicId;
        var now = DateTime.UtcNow;

        account.UpdateProfileImage(
            uploadResult.Url,
            uploadResult.PublicId,
            now);

        await _uow.SaveChangesAsync(ct);

        /*
         * حذف الصورة القديمة من التخزين السحابي غير حرج — الصورة الجديدة
         * محفوظة بالفعل في السطر أعلاه. نلفّه بـ try/catch كي لا يفشل رفع
         * الصورة الناجح بسبب فشل عملية تنظيف لاحقة (نفس نمط الإشعارات غير
         * الحرجة في RateUserCommandHandler وغيرها).
         */
        if (!string.IsNullOrWhiteSpace(previousPublicId) &&
            previousPublicId != uploadResult.PublicId)
        {
            try
            {
                await _storage.DeleteImageAsync(previousPublicId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to delete previous avatar from storage. UserId={UserId}, PreviousPublicId={PreviousPublicId}",
                    request.UserId,
                    previousPublicId);
            }
        }

        return UploadUserAvatarResult.Success(uploadResult.Url!);
    }

    private static async Task<string?> ValidateAvatarFileAsync(
        UploadUserAvatarFileDto file,
        CancellationToken ct)
    {
        if (file.Length == 0)
            return "لم يتم رفع أي صورة.";

        if (file.Length > MaxAvatarSize)
            return "حجم الصورة أكبر من 2 ميجابايت.";

        if (!AllowedImageTypes.Contains(file.ContentType))
            return "نوع الملف غير مدعوم — يُسمح فقط بصور PNG وJPEG.";

        if (!AllowedImageExtensions.Contains(Path.GetExtension(file.FileName)))
            return "امتداد الملف غير مدعوم.";

        if (!await HasValidImageSignatureAsync(file.Content, file.ContentType, ct))
            return "الملف ليس صورة صالحة.";

        return null;
    }

    private static async Task<bool> HasValidImageSignatureAsync(
        Stream content,
        string contentType,
        CancellationToken ct)
    {
        if (!content.CanSeek)
            return false;

        var originalPosition = content.Position;
        var header = new byte[8];
        var bytesRead = await content.ReadAsync(header.AsMemory(0, header.Length), ct);
        content.Position = originalPosition;

        return contentType.ToLowerInvariant() switch
        {
            "image/jpeg" or "image/jpg" => bytesRead >= 3 &&
                header[0] == 0xFF &&
                header[1] == 0xD8 &&
                header[2] == 0xFF,

            "image/png" => bytesRead >= 8 &&
                header[0] == 0x89 &&
                header[1] == 0x50 &&
                header[2] == 0x4E &&
                header[3] == 0x47 &&
                header[4] == 0x0D &&
                header[5] == 0x0A &&
                header[6] == 0x1A &&
                header[7] == 0x0A,

            _ => false
        };
    }
}
