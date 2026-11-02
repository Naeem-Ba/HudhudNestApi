using System;
using System.Linq;

namespace HudhudNestApi.Infrastructure.Auth.Services;

public sealed class SmsProviderOptions
{
    public const string SectionName = "SmsProvider";

    public string Provider { get; init; } = "Http";
    public string ApiUrl { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;
    public string FromNumber { get; init; } = string.Empty;

    /// <summary>Seconds the send-OTP request waits for the provider before treating the send as failed.</summary>
    public int TimeoutSeconds { get; init; } = 10;

    /// <summary>Message template code for providers that only accept registered templates (Unimatrix). Empty = the provider's public Arabic OTP template.</summary>
    public string TemplateId { get; init; } = string.Empty;

    /// <summary>
    /// Calling-code prefixes (for example "+963") a NEW number may have when registering or changing a
    /// number. Empty means no restriction. Existing accounts are never blocked by this list.
    /// </summary>
    public string[] AllowedCountryCodes { get; init; } = [];

    /// <summary>
    /// Production only: an explicit "any country may receive a code" answer. Without it, an empty
    /// <see cref="AllowedCountryCodes"/> stops Production from starting, because the empty default lets anyone
    /// in the world make us pay for an SMS (QA phase 17, P17-D01).
    /// </summary>
    public bool AllowAnyCountry { get; init; }

    /// <summary>
    /// Most one-time codes the API will create in 24 hours for a NEW number (registration, number change),
    /// across all callers. Stops a botnet using many IP addresses from running up the SMS bill; 0 turns the cap off.
    /// Password reset and re-verification of existing accounts are never counted or blocked.
    /// </summary>
    public int DailySendLimit { get; init; } = 300;

    /// <summary>True when the list has at least one entry, or any country was allowed explicitly.</summary>
    public bool HasCountryRestriction =>
        AllowAnyCountry || (AllowedCountryCodes ?? []).Any(x => !string.IsNullOrWhiteSpace(x));

    public bool IsProvider(string name) => string.Equals(Provider?.Trim(), name, StringComparison.OrdinalIgnoreCase);

    /// <summary>The endpoint to call: SmsProvider:ApiUrl, or the provider's public default when it is not set.</summary>
    public string ResolveApiUrl()
    {
        if (!string.IsNullOrWhiteSpace(ApiUrl)) return ApiUrl.Trim();
        if (IsProvider("D7")) return "https://api.d7networks.com/messages/v1/send";
        if (IsProvider("Unimatrix")) return "https://api.unimtx.com/";
        return string.Empty;
    }

    /// <summary>Twilio is configured through its own Twilio:* keys; ApiUrl/ApiKey/FromNumber do not apply to it.</summary>
    public bool UsesSmsProviderKeys => !IsProvider("Twilio");

    /// <summary>Unimatrix treats the sender signature as optional; every other HTTP provider needs a sender id.</summary>
    public bool RequiresFromNumber => UsesSmsProviderKeys && !IsProvider("Unimatrix");

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

        if (!HasCountryRestriction)
        {
            throw new InvalidOperationException(
                "SmsProvider:AllowedCountryCodes must list the calling codes a new number may have (for example " +
                "SmsProvider__AllowedCountryCodes__0=+963), or SmsProvider:AllowAnyCountry must be true to accept any " +
                "country on purpose. An empty list lets anyone in the world make the API send a paid SMS.");
        }

        if (!UsesSmsProviderKeys)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException("SmsProvider:ApiKey is required in Production.");
        }

        if (RequiresFromNumber && string.IsNullOrWhiteSpace(FromNumber))
        {
            throw new InvalidOperationException("SmsProvider:FromNumber is required in Production.");
        }

        if (!Uri.TryCreate(ResolveApiUrl(), UriKind.Absolute, out var endpoint))
        {
            throw new InvalidOperationException("SmsProvider:ApiUrl must be a valid absolute HTTPS URL in Production.");
        }

        if (!string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("SmsProvider:ApiUrl must use HTTPS in Production.");
        }
    }
}
