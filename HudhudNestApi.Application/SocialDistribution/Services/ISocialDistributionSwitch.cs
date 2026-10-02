namespace HudhudNestApi.Application.SocialDistribution.Services;

/// <summary>
/// Single question every outbound social-distribution code path asks first: "is distribution
/// allowed to run at all right now?". See <c>SocialDistributionSwitchOptions</c> for the contract.
/// A narrow interface (not the options type) so a test can flip it at runtime and so the answer
/// can later come from somewhere other than configuration without touching a caller.
/// </summary>
public interface ISocialDistributionSwitch
{
    bool IsEnabled { get; }
}
