namespace HudhudNestApi.Infrastructure.Settings;

public sealed class SocialAuthSettings
{
    public const string SectionName = "SocialAuth";

    public string GoogleClientId { get; init; } = string.Empty;
    public string AppleClientId { get; init; } = string.Empty;
}
