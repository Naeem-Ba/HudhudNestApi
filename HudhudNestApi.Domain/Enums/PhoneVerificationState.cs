namespace HudhudNestApi.Domain.Enums;

public enum PhoneVerificationState
{
    NotConfigured = 0,
    Verified = 1,
    DueSoon = 2,
    GracePeriod = 3,
    Restricted = 4
}
