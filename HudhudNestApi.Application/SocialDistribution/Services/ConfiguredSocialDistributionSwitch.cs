using Microsoft.Extensions.Options;
using HudhudNestApi.Application.SocialDistribution.Options;

namespace HudhudNestApi.Application.SocialDistribution.Services;

/// <summary>Reads <c>SocialDistribution:Enabled</c> through <see cref="IOptionsMonitor{TOptions}"/>, so a reloaded configuration source takes effect without a restart.</summary>
public sealed class ConfiguredSocialDistributionSwitch(IOptionsMonitor<SocialDistributionSwitchOptions> options) : ISocialDistributionSwitch
{
    public bool IsEnabled => options.CurrentValue.Enabled;
}
