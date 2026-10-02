namespace HudhudNestApi.Application.SocialDistribution.Options;

/// <summary>
/// The operator's kill-switch — key <c>SocialDistribution:Enabled</c> (env var
/// <c>SocialDistribution__Enabled</c>). <c>false</c> stops every outbound platform interaction:
/// new publications (event, reconciliation, manual dispatch), sending queued ones (worker, manual
/// publish) and lifecycle calls (edit/comment/delete). Defaults to <c>true</c> so nothing changes
/// until an operator deliberately flips it. Already-live posts are NOT retracted by it.
/// </summary>
public sealed class SocialDistributionSwitchOptions
{
    public const string SectionName = "SocialDistribution";

    public bool Enabled { get; set; } = true;
}
