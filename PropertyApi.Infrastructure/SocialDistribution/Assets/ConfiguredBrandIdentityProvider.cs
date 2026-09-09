using Microsoft.Extensions.Options;
using PropertyApi.Application.SocialDistribution.Interfaces;
using PropertyApi.Domain.SocialDistribution.Models;

namespace PropertyApi.Infrastructure.SocialDistribution.Assets;

public sealed class ConfiguredBrandIdentityProvider : IBrandIdentityProvider
{
    private readonly IOptions<BrandOptions> _options;

    public ConfiguredBrandIdentityProvider(IOptions<BrandOptions> options) => _options = options;

    public BrandIdentity GetCurrentBrand()
    {
        var o = _options.Value;
        return new BrandIdentity(o.Name, o.LogoUrl, o.PrimaryColor, o.SecondaryColor, o.TextColor, o.BackgroundColor, o.FontFamily, o.Version);
    }
}
