using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Auth.Interfaces;
using HudhudNestApi.Application.Common.Observability;

namespace HudhudNestApi.Application.Auth.Services;

/// <summary>
/// Validates password security requirements including complexity and breach screening.
/// Uses Have I Been Pwned API v3 (k-anonymity) to check for breached passwords.
/// </summary>
public sealed class PasswordSecurityService : IPasswordSecurityService
{
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

    public Task<PasswordValidationResult> ValidatePasswordAsync(
        string password,
        CancellationToken cancellationToken = default)
        => ValidatePasswordAsync(password, PasswordCheckContext.None, cancellationToken);

    public async Task<PasswordValidationResult> ValidatePasswordAsync(
        string password,
        PasswordCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // 1 + 2. Every rule that needs no network: complexity, common base words, long runs
        // and the account holder's own name / e-mail / phone digits (PasswordPolicy).
        //
        // This runs before the breach lookup on purpose. CheckBreachedPasswordAsync
        // fails open, so without a local rule the only thing stopping "Password1!" was
        // an external service being reachable. These checks need no network and cannot
        // fail open. If the basic requirements fail the breach database is not asked.
        var localErrors = PasswordPolicy.Evaluate(password, context);
        if (localErrors.Count > 0)
        {
            return PasswordValidationResult.Failure(localErrors);
        }

        // 3. Check against breach database
        var breachError = await CheckBreachedPasswordAsync(password, cancellationToken);

        return breachError is not null
            ? PasswordValidationResult.Failure(breachError)
            : PasswordValidationResult.Success();
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
            request.Headers.Add("User-Agent", "HudhudNestApi-PasswordValidator/1.0");

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
