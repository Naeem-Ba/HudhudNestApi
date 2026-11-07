namespace HudhudNestApi.Application.Agencies.Interfaces;

/// <summary>
/// Lifts the take-downs the office plan sweep made (listings hidden after the 30-day grace period) as soon as the
/// agency owner's plan is renewed, instead of waiting for the next daily sweep. Only the sweep's own take-downs are lifted.
/// </summary>
public interface IAgencyListingVisibility
{
    Task RestoreForRenewedOwnerAsync(Guid ownerUserId, CancellationToken ct = default);
}
