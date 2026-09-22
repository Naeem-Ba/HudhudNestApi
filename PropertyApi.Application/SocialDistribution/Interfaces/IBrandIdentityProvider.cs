using PropertyApi.Domain.SocialDistribution.Models;

namespace PropertyApi.Application.SocialDistribution.Interfaces;

/// <summary>
/// Resolves HudhudNest's current <see cref="BrandIdentity"/> (Phase 7 spec §24) — kept as its own
/// tiny Port so <c>SocialMediaAssetGenerator</c> (Application) never binds directly to an
/// Infrastructure-only <c>IOptions&lt;BrandOptions&gt;</c> configuration type, and so a future
/// "brand managed from an admin screen instead of appsettings" change only touches the
/// Infrastructure implementation.
/// </summary>
public interface IBrandIdentityProvider
{
    BrandIdentity GetCurrentBrand();
}
