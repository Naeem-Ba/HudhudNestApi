namespace PropertyApi.Application.Agencies.DTOs;

/// <summary>
/// Same shape as UploadUserAvatarFileDto (Users) — kept as its own type rather than a
/// shared one so Agencies owns the DTOs at its own boundary, same separation already in
/// place between Reviews/Users/Listings.
/// </summary>
public sealed record SetAgencyLogoFileDto(
    Stream Content,
    string FileName,
    string ContentType,
    long Length);
