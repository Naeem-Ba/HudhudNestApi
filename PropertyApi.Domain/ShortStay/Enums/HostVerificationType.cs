namespace PropertyApi.Domain.ShortStay.Enums;

/// <summary>
/// Manually-admin-set verification badges. Phone/Email badges are deliberately NOT here —
/// they are read directly from the existing user identity/phone-verification state instead
/// of being duplicated (see HostVerificationRecord).
/// </summary>
public enum HostVerificationType
{
    IdentityVerified = 0,
    BusinessVerified = 1,
    PhotosVerified = 2,
    LocationVerified = 3,
}
