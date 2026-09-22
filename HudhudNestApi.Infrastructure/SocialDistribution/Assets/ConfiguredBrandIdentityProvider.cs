using Microsoft.Extensions.Options;
using HudhudNestApi.Application.SocialDistribution.Interfaces;
using HudhudNestApi.Domain.SocialDistribution.Models;

namespace HudhudNestApi.Infrastructure.SocialDistribution.Assets;

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
