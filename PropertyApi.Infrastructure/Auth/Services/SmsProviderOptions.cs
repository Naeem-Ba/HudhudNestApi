namespace PropertyApi.Infrastructure.Auth.Services;

public sealed class SmsProviderOptions
{
    public const string SectionName = "SmsProvider";

    public string Provider { get; init; } = "Http";
    public string ApiUrl { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;
    public string FromNumber { get; init; } = string.Empty;
}
