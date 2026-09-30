using System.Text.RegularExpressions;
using HudhudNestApi.Application.Auth.Interfaces;

namespace HudhudNestApi.Infrastructure.Auth.Security;

public sealed partial class E164PhoneNumberNormalizer : IPhoneNumberNormalizer
{
    public PhoneNumberNormalizationResult Normalize(string? phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber)) return PhoneNumberNormalizationResult.Invalid();
        var candidate = phoneNumber.Trim();
        return E164Pattern().IsMatch(candidate)
            ? PhoneNumberNormalizationResult.Success(candidate)
            : PhoneNumberNormalizationResult.Invalid();
    }

    [GeneratedRegex("^\\+[1-9][0-9]{7,14}$", RegexOptions.CultureInvariant)]
    private static partial Regex E164Pattern();
}
