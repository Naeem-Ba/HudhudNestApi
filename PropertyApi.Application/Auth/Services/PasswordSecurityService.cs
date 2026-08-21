using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.Interfaces;

namespace PropertyApi.Application.Auth.Services;

/// <summary>
/// Validates password security requirements including complexity and breach screening.
/// Uses Have I Been Pwned API v3 (k-anonymity) to check for breached passwords.
/// </summary>
public sealed class PasswordSecurityService : IPasswordSecurityService
{
    private const int MinimumLength = 8;
    private const string SpecialCharacters = "!@#$%^&*()_+-=[]{}|;:',.<>?/~`";

    private readonly HttpClient _httpClient;
    private readonly ILogger<PasswordSecurityService> _logger;

    public PasswordSecurityService(
        HttpClient httpClient,
        ILogger<PasswordSecurityService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<PasswordValidationResult> ValidatePasswordAsync(
        string password,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();

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

        // 2. Check against breach database
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
    private static List<string> ValidateBasicRequirements(string password)
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(password))
        {
            errors.Add("Password is required.");
            return errors;
        }

        // Length check
        if (password.Length < MinimumLength)
        {
            errors.Add($"Password must be at least {MinimumLength} characters long.");
        }

        // Uppercase check
        if (!password.Any(char.IsUpper))
        {
            errors.Add("Password must contain at least one uppercase letter (A-Z).");
        }

        // Lowercase check
        if (!password.Any(char.IsLower))
        {
            errors.Add("Password must contain at least one lowercase letter (a-z).");
        }

        // Digit check
        if (!password.Any(char.IsDigit))
        {
            errors.Add("Password must contain at least one digit (0-9).");
        }

        // Special character check
        if (!password.Any(c => SpecialCharacters.Contains(c)))
        {
            errors.Add($"Password must contain at least one special character: {SpecialCharacters}");
        }

        return errors;
    }

    /// <summary>
    /// Checks if password exists in Have I Been Pwned database using k-anonymity.
    /// https://haveibeenpwned.com/API/v3
    /// </summary>
    private async Task<string?> CheckBreachedPasswordAsync(
        string password,
        CancellationToken cancellationToken)
    {
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

                // Don't block registration on API errors
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var hashes = ParsePwnedResponse(content);

            // Check if our password hash appears in the response
            if (hashes.Contains(hashSuffix))
            {
                _logger.LogWarning(
                    "Password found in Have I Been Pwned database. Registration rejected.");

                return "This password has been exposed in known data breaches. Please choose a different password.";
            }

            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(
                ex,
                "Error checking Have I Been Pwned API. Proceeding with registration.");

            // Don't block registration on network errors
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unexpected error checking password security.");

            // Don't block registration on unexpected errors
            return null;
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
