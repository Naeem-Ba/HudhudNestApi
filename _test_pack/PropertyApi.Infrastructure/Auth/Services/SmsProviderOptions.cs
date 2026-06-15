namespace PropertyApi.Infrastructure.Auth.Services;

public sealed class SmsProviderOptions
{
    public const string SectionName = "SmsProvider";

    public string Provider { get; init; } = "Http";
    public string ApiUrl { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;
    public string FromNumber { get; init; } = string.Empty;

    /// <summary>
    /// Validates SMS provider settings according to the hosting environment.
    /// Production is intentionally stricter: SMS calls must use HTTPS only.
    /// </summary>
    public void ValidateForEnvironment(string environmentName)
    {
        var isProduction = string.Equals(
            environmentName,
            "Production",
            StringComparison.OrdinalIgnoreCase);

        if (!isProduction)
        {
            ValidateOptionalDevelopmentEndpoint();
            return;
        }

        if (string.IsNullOrWhiteSpace(Provider))
            throw new InvalidOperationException("SmsProvider:Provider is required in Production.");

        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new InvalidOperationException("SmsProvider:ApiKey is required in Production.");

        if (string.IsNullOrWhiteSpace(FromNumber))
            throw new InvalidOperationException("SmsProvider:FromNumber is required in Production.");

        if (!Uri.TryCreate(ApiUrl, UriKind.Absolute, out var endpoint))
        {
            throw new InvalidOperationException(
                "SmsProvider:ApiUrl must be a valid absolute HTTPS URL in Production.");
        }

        if (!string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "SmsProvider:ApiUrl must use HTTPS in Production. Plain HTTP SMS endpoints are not allowed.");
        }
    }

    private void ValidateOptionalDevelopmentEndpoint()
    {
        if (string.IsNullOrWhiteSpace(ApiUrl))
            return;

        if (!Uri.TryCreate(ApiUrl, UriKind.Absolute, out var endpoint) ||
            (endpoint.Scheme != Uri.UriSchemeHttps && endpoint.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException(
                "SmsProvider:ApiUrl must be a valid absolute HTTP/HTTPS URL.");
        }
    }
}
