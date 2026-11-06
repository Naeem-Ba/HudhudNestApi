using System.Globalization;
using System.Text;
using HudhudNestApi.Application.Auth.Interfaces;

namespace HudhudNestApi.Application.Auth.Services;

/// <summary>
/// The password rules that need no network, in one place. <see cref="PasswordSecurityService"/>
/// (registration, e-mail reset, change password, phone registration) and the ASP.NET Identity
/// validator (every other path that ends in UserManager) both call it, so a password refused on one
/// path cannot be accepted on another. Until this class existed the phone-number paths were checked
/// by Identity's own options only (length, digit, upper and lower case) and accepted "Abcdefg1" or
/// "Password1!" that the e-mail paths refuse.
///
/// The Angular client mirrors these rules in core/auth/password-policy.ts. The server is the reference:
/// a change here must be copied there, with a test vector on each side.
/// </summary>
public static class PasswordPolicy
{
    public const int MinimumLength = 8;
    public const string SpecialCharacters = "!@#$%^&*()_+-=[]{}|;:',.<>?/~`";
    public const int MaximumRunLength = 4;

    /// <summary>A name or e-mail part shorter than this is never searched for inside a password.</summary>
    private const int MinimumEmbeddedTokenLength = 4;

    /// <summary>
    /// A token of exactly this length ("Ali", "Eva") only counts when it is the whole password once
    /// digits and symbols are trimmed ("Ali123!"). Searching for three letters anywhere would reject
    /// far too many unrelated passwords.
    /// </summary>
    private const int MinimumCoreTokenLength = 3;

    /// <summary>Shortest run of digits taken from a phone number that is refused inside a password.</summary>
    private const int PhoneDigitWindow = 6;

    private static readonly HashSet<string> CommonBaseWords =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "password", "passwort", "passw0rd", "pass", "qwerty", "qwertz", "azerty",
            "asdfgh", "zxcvbn", "qazwsx", "welcome", "admin", "administrator", "root",
            "login", "letmein", "changeme", "secret", "default", "guest", "test",
            "monkey", "dragon", "sunshine", "princess", "shadow", "master", "superman",
            "batman", "football", "baseball", "iloveyou", "trustno", "whatever",
            "freedom", "starwars", "computer", "internet", "abcdef", "abc"
        };

    private static readonly char[] DecorationCharacters =
        ("0123456789" + SpecialCharacters).ToCharArray();

    /// <summary>
    /// Every local rule, in the order the client lists them. The basic rules come first; the pattern
    /// rules (common word, long run, personal information) are only reported once the basic ones pass,
    /// exactly as before, so the first screen of messages stays short and actionable.
    /// </summary>
    public static IReadOnlyList<PasswordValidationError> Evaluate(
        string? password,
        PasswordCheckContext? context = null)
    {
        var basic = ValidateBasicRequirements(password);
        if (basic.Count > 0)
        {
            return basic;
        }

        return ValidatePatterns(password!, context);
    }

    public static List<PasswordValidationError> ValidateBasicRequirements(string? password)
    {
        var errors = new List<PasswordValidationError>();

        if (string.IsNullOrEmpty(password))
        {
            errors.Add(new PasswordValidationError(
                PasswordErrorCodes.Required,
                "Password is required."));
            return errors;
        }

        if (password.Length < MinimumLength)
        {
            errors.Add(new PasswordValidationError(
                PasswordErrorCodes.TooShort,
                $"Password must be at least {MinimumLength} characters long."));
        }

        if (!password.Any(char.IsUpper))
        {
            errors.Add(new PasswordValidationError(
                PasswordErrorCodes.NoUppercase,
                "Password must contain at least one uppercase letter (A-Z)."));
        }

        if (!password.Any(char.IsLower))
        {
            errors.Add(new PasswordValidationError(
                PasswordErrorCodes.NoLowercase,
                "Password must contain at least one lowercase letter (a-z)."));
        }

        if (!password.Any(char.IsDigit))
        {
            errors.Add(new PasswordValidationError(
                PasswordErrorCodes.NoDigit,
                "Password must contain at least one digit (0-9)."));
        }

        if (!password.Any(c => SpecialCharacters.Contains(c)))
        {
            errors.Add(new PasswordValidationError(
                PasswordErrorCodes.NoSpecialCharacter,
                $"Password must contain at least one special character: {SpecialCharacters}"));
        }

        return errors;
    }

    public static List<PasswordValidationError> ValidatePatterns(
        string password,
        PasswordCheckContext? context = null)
    {
        var errors = new List<PasswordValidationError>();

        // One combined trim, not digits-then-symbols: trimming in two passes leaves
        // "Password1!" as "Password1", because the trailing '!' shields the '1' from
        // the digit pass and the '1' is then no longer at the end for the symbol pass.
        var core = password.Trim(DecorationCharacters);

        if (CommonBaseWords.Contains(core))
        {
            errors.Add(new PasswordValidationError(
                PasswordErrorCodes.CommonWord,
                "Password is based on a commonly used word. Adding digits or symbols to " +
                "it does not make it harder to guess."));
        }

        if (HasLongRun(password))
        {
            errors.Add(new PasswordValidationError(
                PasswordErrorCodes.LongRun,
                $"Password contains a run of more than {MaximumRunLength} sequential or " +
                "repeated characters."));
        }

        if (ContainsPersonalInformation(password, context))
        {
            errors.Add(new PasswordValidationError(
                PasswordErrorCodes.PersonalInfo,
                "Password must not contain your name, the first part of your e-mail address " +
                "or part of your phone number."));
        }

        return errors;
    }

    /// <summary>
    /// True when the password contains the account holder's own name, the name part of their
    /// e-mail address, or a run of their phone number.
    ///
    /// Deliberately not a plain substring test on the raw text, which would both miss "M0hammad" and
    /// "ÁLI" and reject unrelated passwords:
    ///  * both sides are folded first (case, accents, Arabic diacritics and alef/hamza forms,
    ///    Arabic-Indic digits), and the password is also tried with the usual look-alike swaps
    ///    (0->o, 1->i, 3->e, 4->a, 5->s, 7->t, @->a, $->s);
    ///  * a name or e-mail part of four or more letters is searched for anywhere; one of exactly three
    ///    ("Ali") only when it is the whole password once digits and symbols are trimmed;
    ///    anything shorter is ignored;
    ///  * a phone number only matters through six consecutive digits it really contains.
    /// Only values the caller actually has are used. There is no date of birth anywhere in the
    /// account model, so none is checked.
    /// </summary>
    public static bool ContainsPersonalInformation(string password, PasswordCheckContext? context)
    {
        if (context is null || string.IsNullOrEmpty(password))
        {
            return false;
        }

        var folded = Fold(password);
        var deleeted = Deleet(folded);

        var tokens = CollectTextTokens(context);
        foreach (var token in tokens)
        {
            if (token.Length >= MinimumEmbeddedTokenLength)
            {
                if (folded.Contains(token, StringComparison.Ordinal) ||
                    deleeted.Contains(token, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            else if (token.Length == MinimumCoreTokenLength)
            {
                if (folded.Trim(DecorationCharacters) == token ||
                    deleeted.Trim(DecorationCharacters) == token)
                {
                    return true;
                }
            }
        }

        return ContainsPhoneDigits(folded, context.PhoneNumber);
    }

    private static HashSet<string> CollectTextTokens(PasswordCheckContext context)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);

        AddWords(tokens, context.FirstName);
        AddWords(tokens, context.LastName);

        var email = !string.IsNullOrWhiteSpace(context.Email)
            ? context.Email
            : context.UserName is { } user && user.Contains('@') ? user : null;

        if (email is not null)
        {
            var at = email.IndexOf('@');
            var local = at > 0 ? email[..at] : email;

            AddWords(tokens, local);

            // "john.smith" -> also the joined form, which is what people actually type.
            var joined = new string(Fold(local).Where(char.IsLetter).ToArray());
            if (joined.Length >= MinimumEmbeddedTokenLength)
            {
                tokens.Add(joined);
            }
        }

        return tokens;
    }

    private static void AddWords(HashSet<string> tokens, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var current = new StringBuilder();
        foreach (var ch in Fold(value))
        {
            if (char.IsLetter(ch))
            {
                current.Append(ch);
                continue;
            }

            Flush(tokens, current);
        }

        Flush(tokens, current);
    }

    private static void Flush(HashSet<string> tokens, StringBuilder current)
    {
        if (current.Length >= MinimumCoreTokenLength)
        {
            tokens.Add(current.ToString());
        }

        current.Clear();
    }

    private static bool ContainsPhoneDigits(string foldedPassword, string? phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return false;
        }

        var phoneDigits = new string(Fold(phoneNumber).Where(char.IsAsciiDigit).ToArray());
        if (phoneDigits.Length < PhoneDigitWindow)
        {
            return false;
        }

        var run = new StringBuilder();
        foreach (var ch in foldedPassword.Append('\0'))
        {
            if (char.IsAsciiDigit(ch))
            {
                run.Append(ch);
                continue;
            }

            if (run.Length >= PhoneDigitWindow)
            {
                var digits = run.ToString();
                for (var i = 0; i + PhoneDigitWindow <= digits.Length; i++)
                {
                    if (phoneDigits.Contains(digits.AsSpan(i, PhoneDigitWindow), StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            run.Clear();
        }

        return false;
    }

    /// <summary>
    /// Lower case, accents and Arabic diacritics removed, alef/hamza/ya/ta-marbuta variants unified,
    /// Arabic-Indic digits turned into ASCII digits.
    /// </summary>
    internal static string Fold(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormKD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var original in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(original);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.Format or UnicodeCategory.EnclosingMark)
            {
                continue;
            }

            var ch = char.ToLowerInvariant(original);

            switch (ch)
            {
                case 'ـ': // tatweel
                    continue;
                case 'أ':
                case 'إ':
                case 'آ':
                case 'ٱ':
                    ch = 'ا'; // alef variants -> alef
                    break;
                case 'ى':
                case 'ئ':
                    ch = 'ي'; // alef maqsura, hamza on ya -> ya
                    break;
                case 'ؤ':
                    ch = 'و'; // hamza on waw -> waw
                    break;
                case 'ة':
                    ch = 'ه'; // ta marbuta -> ha
                    break;
                default:
                    if (ch is >= '٠' and <= '٩')
                    {
                        ch = (char)('0' + (ch - '٠'));
                    }
                    else if (ch is >= '۰' and <= '۹')
                    {
                        ch = (char)('0' + (ch - '۰'));
                    }

                    break;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    private static string Deleet(string folded)
    {
        var builder = new StringBuilder(folded.Length);
        foreach (var ch in folded)
        {
            builder.Append(ch switch
            {
                '0' => 'o',
                '1' => 'i',
                '3' => 'e',
                '4' => 'a',
                '5' => 's',
                '7' => 't',
                '@' => 'a',
                '$' => 's',
                _ => ch
            });
        }

        return builder.ToString();
    }

    /// <summary>
    /// True when the password contains more than <see cref="MaximumRunLength"/>
    /// consecutive characters that ascend ("123456"), descend ("54321"), or repeat
    /// ("aaaaa"). The threshold is deliberately above the length of a year or a short
    /// numeric suffix, so "MyPassword123!" is unaffected while "Aa123456!" is not.
    /// </summary>
    public static bool HasLongRun(string password)
    {
        var ascending = 1;
        var descending = 1;
        var repeating = 1;

        for (var i = 1; i < password.Length; i++)
        {
            var delta = password[i] - password[i - 1];

            ascending = delta == 1 ? ascending + 1 : 1;
            descending = delta == -1 ? descending + 1 : 1;
            repeating = delta == 0 ? repeating + 1 : 1;

            if (ascending > MaximumRunLength ||
                descending > MaximumRunLength ||
                repeating > MaximumRunLength)
            {
                return true;
            }
        }

        return false;
    }
}
