using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HudhudNestApi.Application.Common.Enums;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Common.Models;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Application.SocialDistribution.DTOs;
using HudhudNestApi.Application.SocialDistribution.Interfaces;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.SocialDistribution.Entities;
using HudhudNestApi.Domain.SocialDistribution.Enums;
using HudhudNestApi.Domain.SocialDistribution.Models;
using HudhudNestApi.Domain.SocialDistribution.Policies;
using HudhudNestApi.Domain.SocialDistribution.Templates;

namespace HudhudNestApi.Application.SocialDistribution.Services;

/// <summary>
/// Orchestrates Phase 7's deterministic Template Engine (spec §17): resolves the platform preset,
/// loads the property facts the template needs, renders the SVG via the pure
/// <see cref="SocialAssetTemplateRenderer"/>, computes a checksum, reuses an identical
/// previously-generated asset when one exists, otherwise uploads and persists a new
/// <see cref="SocialMediaAsset"/>.
///
/// Never calls an AI model (spec rule 11 / §25: the future AI extension point is
/// <c>ISocialContentGenerator</c>, a wholly separate, not-yet-implemented port for TEXT — this
/// class only ever produces images from the fixed template).
///
/// Current output format: SVG (<c>image/svg+xml</c>) — see <see cref="SocialAssetTemplateRenderer"/>'s
/// remarks for why this is the correct "deterministic, testable, no server-side font
/// installation" choice for this phase, and for the documented follow-up (a real raster
/// PNG/JPEG conversion step) needed before a platform whose real API rejects SVG uploads can be
/// wired to a genuine publisher.
/// </summary>
public sealed class SocialMediaAssetGenerator : ISocialMediaAssetGenerator
{
    private readonly IPropertyRepository _properties;
    private readonly ISocialMediaAssetRepository _assets;
    private readonly ISocialMediaAssetStorage _storage;
    private readonly IMediaFolderBuilder _folderBuilder;
    private readonly IBrandIdentityProvider _brandProvider;
    private readonly IUnitOfWork _uow;

    public SocialMediaAssetGenerator(
        IPropertyRepository properties,
        ISocialMediaAssetRepository assets,
        ISocialMediaAssetStorage storage,
        IMediaFolderBuilder folderBuilder,
        IBrandIdentityProvider brandProvider,
        IUnitOfWork uow)
    {
        _properties = properties;
        _assets = assets;
        _storage = storage;
        _folderBuilder = folderBuilder;
        _brandProvider = brandProvider;
        _uow = uow;
    }

    public async Task<GeneratedSocialAssetResult> GenerateAsync(
        GenerateSocialAssetRequest request, bool forceRegenerate = false, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var property = await _properties.GetByIdWithDetailsAsync(request.PropertyId, ct)
            ?? throw new SocialAssetGenerationException("العقار غير موجود، تعذّر توليد صورة اجتماعية له.", retryable: false);

        var assetType = request.AssetType ?? SocialAssetPresetCatalog.GetDefaultAssetType(request.Platform);
        var preset = SocialAssetPresetCatalog.TryGetPreset(request.Platform, assetType)
            ?? throw new SocialAssetGenerationException(
                $"لا يوجد قالب أبعاد معرَّف لهذه المنصة/النوع ({request.Platform}/{assetType}).", retryable: false);

        var sourceImageUrl = request.ImageUrls.FirstOrDefault(url => SocialContentPolicy.IsValidPublicUrl(url));

        var context = new SocialAssetTemplateContext(
            request.PropertyId,
            SocialAssetTemplateRenderer.TemplateId,
            SocialAssetTemplateRenderer.TemplateVersion,
            request.Platform,
            assetType,
            request.Language,
            request.Title,
            BuildLocation(property),
            BuildPrice(property, request.Language),
            BuildArea(property, request.Language),
            BuildRooms(property, request.Language),
            property.PropertyType?.NameAr,
            BuildTransactionTypeLabel(property.ListingType, request.Language),
            sourceImageUrl,
            _brandProvider.GetCurrentBrand());

        string svg;
        try
        {
            svg = SocialAssetTemplateRenderer.Render(context, preset);
        }
        catch (Exception ex)
        {
            // The renderer is pure/deterministic — reaching this means a programming error, not a
            // transient condition, so it is never worth retrying automatically.
            throw new SocialAssetGenerationException("فشل تصيير قالب الصورة الاجتماعية.", retryable: false, ex);
        }

        var bytes = Encoding.UTF8.GetBytes(svg);
        var checksum = Convert.ToHexString(SHA256.HashData(bytes));

        if (!forceRegenerate)
        {
            var reusable = await _assets.FindReusableAsync(
                request.PropertyId, request.Platform, assetType,
                SocialAssetTemplateRenderer.TemplateId, SocialAssetTemplateRenderer.TemplateVersion, checksum, ct);

            if (reusable is not null)
                return ToResult(reusable, reused: true);
        }

        (string Url, string StorageKey) uploaded;
        try
        {
            var fileName = $"{request.PropertyId:N}-{request.Platform}-{assetType}-{checksum[..12]}.svg";
            var folder = _folderBuilder.BuildFolder(MediaEntityType.Social, request.PropertyId, MediaCategories.Share);
            uploaded = await _storage.SaveAsync(bytes, fileName, "image/svg+xml", folder, ct);
        }
        catch (Exception ex)
        {
            // A storage/network failure IS worth retrying — the rendered content itself was fine.
            throw new SocialAssetGenerationException("تعذّر رفع الصورة الاجتماعية المولّدة إلى التخزين.", retryable: true, ex);
        }

        var asset = SocialMediaAsset.Create(
            request.PropertyId, request.Platform, assetType,
            SocialAssetTemplateRenderer.TemplateId, SocialAssetTemplateRenderer.TemplateVersion,
            uploaded.Url, uploaded.StorageKey, preset.Width, preset.Height, "image/svg+xml", bytes.LongLength, checksum);

        await _assets.AddAsync(asset, ct);
        await _uow.SaveChangesAsync(ct);

        return ToResult(asset, reused: false);
    }

    private static GeneratedSocialAssetResult ToResult(SocialMediaAsset asset, bool reused) => new(
        asset.Id, asset.Platform, asset.AssetType, asset.FileUrl, asset.Width, asset.Height,
        asset.MimeType, asset.FileSizeBytes, asset.Checksum, asset.TemplateId, asset.TemplateVersion, reused);

    private static string? BuildLocation(Domain.Listings.Entities.Property property)
    {
        var parts = new List<string>();
        if (property.Governorate is not null) parts.Add(property.Governorate.NameAr);
        else if (!string.IsNullOrWhiteSpace(property.City)) parts.Add(property.City);

        return parts.Count == 0 ? null : string.Join("، ", parts);
    }

    private static string? BuildPrice(Domain.Listings.Entities.Property property, string language)
    {
        var amount = property.PurchasePrice ?? property.ColdRent ?? property.WarmRent;
        return amount is null or <= 0 ? null : SocialAssetTemplateRenderer.FormatPrice(amount.Value, property.CurrencyCode);
    }

    private static string? BuildArea(Domain.Listings.Entities.Property property, string language)
    {
        if (property.Area is not > 0)
            return null;

        var unit = language.Equals("ar", StringComparison.OrdinalIgnoreCase) ? "م²" : "m²";
        return string.Format(CultureInfo.InvariantCulture, "{0} {1}", property.Area, unit);
    }

    private static string? BuildRooms(Domain.Listings.Entities.Property property, string language)
    {
        if (property.Rooms is not > 0)
            return null;

        return language.Equals("ar", StringComparison.OrdinalIgnoreCase)
            ? $"{property.Rooms} غرف"
            : $"{property.Rooms} rooms";
    }

    private static string? BuildTransactionTypeLabel(ListingType listingType, string language)
    {
        var isArabic = language.Equals("ar", StringComparison.OrdinalIgnoreCase);
        return listingType switch
        {
            ListingType.ForSale => isArabic ? "للبيع" : "For Sale",
            ListingType.ForRent => isArabic ? "للإيجار" : "For Rent",
            ListingType.ForRentAndSale => isArabic ? "للبيع والإيجار" : "For Sale/Rent",
            _ => null,
        };
    }
}
