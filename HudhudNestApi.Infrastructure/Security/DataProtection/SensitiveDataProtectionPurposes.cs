namespace HudhudNestApi.Infrastructure.Security.DataProtection;

public static class SensitiveDataProtectionPurposes
{
    public const string UserPhoneNumber = "HudhudNestApi.Users.PhoneNumber.v1";
    public const string UserWhatsAppNumber = "HudhudNestApi.Users.WhatsAppNumber.v1";
    public const string UserTaxNumber = "HudhudNestApi.Users.TaxNumber.v1";
    public const string SocialAccountCredentialReference = "HudhudNestApi.SocialDistribution.SocialAccount.CredentialReference.v1";
}
