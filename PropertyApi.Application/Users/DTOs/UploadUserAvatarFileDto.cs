namespace PropertyApi.Application.Users.DTOs;

/// <summary>
/// نفس بنية UploadPropertyImageFileDto (Listings) لكن بنسخة خاصة بالمستخدمين —
/// أُبقيت منفصلة عمدًا بدل إعادة استخدام نوع Listings عبر طبقات، فكل Feature
/// يملك DTOs الخاصة بحدوده (نفس الفصل المعمول به بالفعل بين Reviews وUsers).
/// </summary>
public sealed record UploadUserAvatarFileDto(
    Stream Content,
    string FileName,
    string ContentType,
    long Length);
