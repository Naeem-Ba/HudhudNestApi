namespace PropertyApi.Application.Users.DTOs;

public enum UploadUserAvatarStatus
{
    Success,
    NotFound,
    ValidationFailed,
    StorageFailed
}

/// <summary>
/// نفس نمط PropertyImageMutationResult (Listings) — Status صريح بدل رمي
/// استثناءات لحالات "متوقعة" (نوع/حجم غير مسموح، فشل التخزين)، تاركين
/// الاستثناءات للحالات غير المتوقعة فعلاً.
/// </summary>
public sealed record UploadUserAvatarResult(
    UploadUserAvatarStatus Status,
    string? ImageUrl = null,
    string? Message = null)
{
    public static UploadUserAvatarResult Success(string imageUrl) =>
        new(UploadUserAvatarStatus.Success, imageUrl);

    public static UploadUserAvatarResult NotFound(string? message = null) =>
        new(UploadUserAvatarStatus.NotFound, null, message);

    public static UploadUserAvatarResult ValidationFailed(string message) =>
        new(UploadUserAvatarStatus.ValidationFailed, null, message);

    public static UploadUserAvatarResult StorageFailed(string? message) =>
        new(UploadUserAvatarStatus.StorageFailed, null, message);
}
