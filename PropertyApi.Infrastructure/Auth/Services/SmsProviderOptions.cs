using System;

namespace PropertyApi.Infrastructure.Auth.Services;

public sealed class SmsProviderOptions
{
    public const string SectionName = "SmsProvider";

    public string Provider { get; init; } = "Http";
    public string ApiUrl { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;
    public string FromNumber { get; init; } = string.Empty;

    /// <summary>Seconds the send-OTP request waits for the provider before treating the send as failed.</summary>
    public int TimeoutSeconds { get; init; } = 10;

    /// <summary>
    /// Calling-code prefixes (for example "+963") a NEW number may have when registering or changing a
    /// number. Empty means no restriction. Existing accounts are never blocked by this list.
    /// </summary>
    public string[] AllowedCountryCodes { get; init; } = [];

    public void ValidateForEnvironment(string environmentName)
    {
        var isProduction = string.Equals(
            environmentName,
            "Production",
            StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(Provider))
        {
            throw new InvalidOperationException("SmsProvider:Provider is required.");
        }

        if (!isProduction)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException("SmsProvider:ApiKey is required in Production.");
        }

        if (string.IsNullOrWhiteSpace(FromNumber))
        {
            throw new InvalidOperationException("SmsProvider:FromNumber is required in Production.");
        }

        if (!Uri.TryCreate(ApiUrl, UriKind.Absolute, out var endpoint))
        {
            throw new InvalidOperationException("SmsProvider:ApiUrl must be a valid absolute HTTPS URL in Production.");
        }

        if (!string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("SmsProvider:ApiUrl must use HTTPS in Production.");
        }
    }
}
