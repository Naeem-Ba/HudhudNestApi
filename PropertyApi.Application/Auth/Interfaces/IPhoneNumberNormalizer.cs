namespace PropertyApi.Application.Auth.Interfaces;

public interface IPhoneNumberNormalizer
{
    PhoneNumberNormalizationResult Normalize(string? phoneNumber);
}

public sealed record PhoneNumberNormalizationResult(bool Succeeded, string? Value, string? ErrorCode)
{
    public static PhoneNumberNormalizationResult Success(string value) => new(true, value, null);
    public static PhoneNumberNormalizationResult Invalid() => new(false, null, "PHONE_NUMBER_INVALID");
}
