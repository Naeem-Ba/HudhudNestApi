using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Common.Observability;

namespace PropertyApi.Application.Auth.Services;

/// <summary>
/// Validates password security requirements including complexity and breach screening.
/// Uses Have I Been Pwned API v3 (k-anonymity) to check for breached passwords.
/// </summary>
public sealed class PasswordSecurityService : IPasswordSecurityService
{
    private const int MinimumLength = 8;
    private const string SpecialCharacters = "!@#$%^&*()_+-=[]{}|;:',.<>?/~`";

    /// <summary>
    /// Base words that make a password guessable no matter how the character-class
    /// rules are satisfied. Matched against the password's *core* -- the value with
    /// leading and trailing non-letters stripped -- so "Password1!" and "Qwerty123!"
    /// are rejected while "MyPassword123!" and "SecurePass123!" are not: prepending a
    /// real word is what moves a password out of the top of every cracking dictionary.
    /// </summary>
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

    private const int MaximumRunLength = 4;

    private readonly HttpClient _httpClient;
    private readonly ILogger<PasswordSecurityService> _logger;
    private readonly PwnedPasswordsCircuitBreaker _circuitBreaker;

    public PasswordSecurityService(
        HttpClient httpClient,
        ILogger<PasswordSecurityService> logger,
        PwnedPasswordsCircuitBreaker circuitBreaker)
    {
        _httpClient = httpClient;
        _logger = logger;
        _circuitBreaker = circuitBreaker;
    }

    public async Task<PasswordValidationResult> ValidatePasswordAsync(
        string password,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<PasswordValidationError>();

        // 1. Check basic requirements
        var basicErrors = ValidateBasicRequirements(password);
        if (basicErrors.Any())
        {
            errors.AddRange(basicErrors);
        }

        // If basic requirements fail, return early without checking breach database
        if (errors.Any())
        {
            return PasswordValidationResult.Failure(errors);
        }

        // 2. Reject guessable patterns locally.
        //
        // This runs before the breach lookup on purpose. CheckBreachedPasswordAsync
        // fails open, so without a local rule the only thing stopping "Password1!" was
        // an external service being reachable. These checks need no network and cannot
        // fail open.
        var patternErrors = ValidateCommonPatterns(password);
        if (patternErrors.Any())
        {
            return PasswordValidationResult.Failure(patternErrors);
        }

        // 3. Check against breach database
        var breachError = await CheckBreachedPasswordAsync(password, cancellationToken);
        if (breachError is not null)
        {
            errors.Add(breachError);
        }

        return errors.Any()
            ? PasswordValidationResult.Failure(errors)
            : PasswordValidationResult.Success();
    }

    /// <summary>
    /// Validates basic password complexity requirements.
    /// </summary>
    private static List<PasswordValidationError> ValidateBasicRequirements(string password)
    {
        var errors = new List<PasswordValidationError>();

        if (string.IsNullOrEmpty(password))
        {
            errors.Add(new PasswordValidationError(
                PasswordErrorCodes.Required,
                "Password is required."));
            return errors;
        }

        // Length check
        if (password.Length < MinimumLength)
        {
            errors.Add(new PasswordValidationError(
                PasswordErrorCodes.TooShort,
                $"Password must be at least {MinimumLength} characters long."));
        }

        // Uppercase check
        if (!password.Any(char.IsUpper))
        {
            errors.Add(new PasswordValidationError(
                PasswordErrorCodes.NoUppercase,
                "Password must contain at least one uppercase letter (A-Z)."));
        }

        // Lowercase check
        if (!password.Any(char.IsLower))
        {
            errors.Add(new PasswordValidationError(
                PasswordErrorCodes.NoLowercase,
                "Password must contain at least one lowercase letter (a-z)."));
        }

        // Digit check
        if (!password.Any(char.IsDigit))
        {
            errors.Add(new PasswordValidationError(
                PasswordErrorCodes.NoDigit,
                "Password must contain at least one digit (0-9)."));
        }

        // Special character check
        if (!password.Any(c => SpecialCharacters.Contains(c)))
        {
            errors.Add(new PasswordValidationError(
                PasswordErrorCodes.NoSpecialCharacter,
                $"Password must contain at least one special character: {SpecialCharacters}"));
        }

        return errors;
    }

    /// <summary>
    /// Locally detectable weakness: a password that is a common base word dressed up
    /// with digits and punctuation, or one carrying a long sequential/repeated run.
    /// </summary>
    private static readonly char[] DecorationCharacters =
        ("0123456789" + SpecialCharacters).ToCharArray();

    private static List<PasswordValidationError> ValidateCommonPatterns(string password)
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

        return errors;
    }

    /// <summary>
    /// True when the password contains more than <see cref="MaximumRunLength"/>
    /// consecutive characters that ascend ("123456"), descend ("54321"), or repeat
    /// ("aaaaa"). The threshold is deliberately above the length of a year or a short
    /// numeric suffix, so "MyPassword123!" is unaffected while "Aa123456!" is not.
    /// </summary>
    private static bool HasLongRun(string password)
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

    /// <summary>
    /// Checks if password exists in Have I Been Pwned database using k-anonymity.
    /// https://haveibeenpwned.com/API/v3
    /// </summary>
    private async Task<PasswordValidationError?> CheckBreachedPasswordAsync(
        string password,
        CancellationToken cancellationToken)
    {
        // The screening still fails open -- an outage must not block registration --
        // but every skipped check is now counted, so "no breached passwords rejected
        // lately" can be told apart from "screening has been silently off for a week".
        if (!_circuitBreaker.ShouldAttempt())
        {
            ApplicationTelemetry.RecordPasswordBreachScreening("circuit_open");
            return null;
        }

        try
        {
            // Calculate SHA-1 hash of the password
            var sha1Hash = ComputeSha1Hash(password);

            // Use only the first 5 characters (k-anonymity)
            var hashPrefix = sha1Hash.Substring(0, 5);
            var hashSuffix = sha1Hash.Substring(5).ToUpper();

            // Call Have I Been Pwned API
            var url = $"https://api.pwnedpasswords.com/range/{hashPrefix}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "PropertyApi-PasswordValidator/1.0");

            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Have I Been Pwned API returned status {StatusCode}. Proceeding with registration.",
                    response.StatusCode);

                RecordScreeningFailure($"status {(int)response.StatusCode}");

                // Don't block registration on API errors
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var hashes = ParsePwnedResponse(content);

            _circuitBreaker.RecordSuccess();

            // Check if our password hash appears in the response
            if (hashes.Contains(hashSuffix))
            {
                _logger.LogWarning(
                    "Password found in Have I Been Pwned database. Registration rejected.");

                ApplicationTelemetry.RecordPasswordBreachScreening("breached");

                return new PasswordValidationError(
                    PasswordErrorCodes.Breached,
                    "This password has been exposed in known data breaches. " +
                    "Please choose a different password.");
            }

            ApplicationTelemetry.RecordPasswordBreachScreening("clean");
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller went away. Not an outage, so it must not trip the breaker.
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(
                ex,
                "Error checking Have I Been Pwned API. Proceeding with registration.");

            RecordScreeningFailure(ex.GetType().Name);

            // Don't block registration on network errors
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unexpected error checking password security.");

            RecordScreeningFailure(ex.GetType().Name);

            // Don't block registration on unexpected errors
            return null;
        }
    }

    private void RecordScreeningFailure(string reason)
    {
        ApplicationTelemetry.RecordPasswordBreachScreening("unavailable");

        if (_circuitBreaker.RecordFailure())
        {
            _logger.LogError(
                "Password breach screening is unavailable after {Threshold} consecutive " +
                "failures (last: {Reason}). Skipping the Have I Been Pwned lookup for " +
                "{OpenSeconds}s. Passwords are being accepted without breach screening " +
                "during this window; local complexity and common-pattern rules still apply.",
                PwnedPasswordsCircuitBreaker.FailureThreshold,
                reason,
                PwnedPasswordsCircuitBreaker.OpenDuration.TotalSeconds);
        }
    }

    /// <summary>
    /// Computes SHA-1 hash of the password.
    /// </summary>
    private static string ComputeSha1Hash(string input)
    {
        using var sha1 = SHA1.Create();
        var hashedBytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hashedBytes);
    }

    /// <summary>
    /// Parses the Have I Been Pwned response format.
    /// Format: "SUFFIX:COUNT\r\n" (one per line)
    /// </summary>
    private static HashSet<string> ParsePwnedResponse(string content)
    {
        var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lines = content.Split(
            new[] { "\r\n", "\n" },
            StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var parts = line.Split(':');
            if (parts.Length > 0)
            {
                hashes.Add(parts[0].Trim());
            }
        }

        return hashes;
    }
}
