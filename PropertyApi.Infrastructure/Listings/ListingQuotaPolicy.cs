using Microsoft.Extensions.Options;
using PropertyApi.Application.Listings.Interfaces;

namespace PropertyApi.Infrastructure.Listings;

/// <summary>
/// Adapts the live <see cref="ListingQuotaOptions"/> to the Application-facing
/// <see cref="IListingQuotaPolicy"/> seam, so CreatePropertyCommandHandler never depends on
/// the options/configuration packages directly.
/// </summary>
internal sealed class ListingQuotaPolicy : IListingQuotaPolicy
{
    private readonly IOptionsMonitor<ListingQuotaOptions> _options;

    public ListingQuotaPolicy(IOptionsMonitor<ListingQuotaOptions> options)
    {
        _options = options;
    }

    public int AgencyActiveListingLimit =>
        // Defensive floor: a bad edit (0 or negative) in appsettings.json must not silently
        // block every agency in the country from ever posting again.
        Math.Max(1, _options.CurrentValue.AgencyActiveListingLimit);
}
