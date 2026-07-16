namespace PropertyApi.Domain.Enums;

public enum OtpPurpose
{
    PhoneRegistration = 1,

    [Obsolete("Phone login now uses a password. This value remains for stored-data compatibility.")]
    PhoneLogin = 2,

    PhonePasswordReset = 3,
    PhoneReverification = 4,
    PhoneNumberChange = 5
}
