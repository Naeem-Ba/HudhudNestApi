using System.Globalization;
using MediatR;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Application.SocialDistribution.AiContent;
using HudhudNestApi.Application.SocialDistribution.DTOs;
using HudhudNestApi.Application.SocialDistribution.Interfaces;
using HudhudNestApi.Application.SocialDistribution.Mapping;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Domain.SocialDistribution.Entities;

namespace HudhudNestApi.Application.SocialDistribution.Commands.CreateSocialPublication;

/// <summary>
/// Enforces every creation-time invariant from spec §9 (public property, active account, active
/// channel, platform match), builds the UTM-attributed TargetUrl, auto-fills any content field
/// the caller left out from the property itself, and persists Draft publication + content +
/// the first status-history row in one transaction.
/// </summary>
public sealed class CreateSocialPublicationCommandHandler
    : IRequestHandler<CreateSocialPublicationCommand, SocialPublicationDto>
{
    private readonly IPropertyRepository _properties;
    private readonly ISocialAccountRepository _accounts;
    private readonly ISocialChannelRepository _channels;
    private readonly ISocialPublicationRepository _publications;
    private readonly ISocialPublicationStatusHistoryRepository _history;
    private readonly ISocialDistributionTargetUrlBuilder _urlBuilder;
    private readonly ISocialContentGenerator _contentGenerator;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<CreateSocialPublicationCommandHandler>? _logger;

    public CreateSocialPublicationCommandHandler(
        IPropertyRepository properties,
        ISocialAccountRepository accounts,
        ISocialChannelRepository channels,
        ISocialPublicationRepository publications,
        ISocialPublicationStatusHistoryRepository history,
        ISocialDistributionTargetUrlBuilder urlBuilder,
        ISocialContentGenerator contentGenerator,
        IUnitOfWork uow,
        ILogger<CreateSocialPublicationCommandHandler>? logger = null)
    {
        _properties = properties;
        _accounts = accounts;
        _channels = channels;
        _publications = publications;
        _history = history;
        _urlBuilder = urlBuilder;
        _contentGenerator = contentGenerator;
        _uow = uow;
        _logger = logger;
    }

    public async Task<SocialPublicationDto> Handle(CreateSocialPublicationCommand request, CancellationToken ct)
    {
        // Spec invariant §9.1: only a public, published property may be distributed.
        var property = await _properties.GetPublishedByIdWithDetailsAsync(request.PropertyId, ct)
            ?? throw new ConflictException("لا يمكن إنشاء منشور توزيع لعقار غير منشور أو غير عام أو غير موجود.");

        var account = await _accounts.GetByIdAsync(request.SocialAccountId, ct)
            ?? throw new NotFoundException("الحساب الاجتماعي غير موجود.");

        // Spec invariant §9.2: only an Active account may be targeted.
        if (!account.CanPublish())
            throw new ConflictException("لا يمكن إنشاء منشور توزيع لحساب اجتماعي غير فعّال.");

        var channel = await _channels.GetByIdAsync(account.SocialChannelId, ct)
            ?? throw new NotFoundException("القناة الاجتماعية غير موجودة.");

        // Spec invariant §9.3: only an Active channel may back a new publication.
        if (!channel.CanBackNewAccounts())
            throw new ConflictException("لا يمكن إنشاء منشور توزيع عبر قناة غير فعّالة.");

        // Spec invariant §9.4: the account's platform must match the channel it belongs to —
        // guaranteed structurally here since SocialAccount.Platform is copied from its channel
        // at creation time and never changes afterwards (see SocialAccount.Create).

        var publication = SocialPublication.Create(
            property.Id,
            account.Id,
            request.CreatedByUserId,
            account.Platform,
            distributionRuleId: request.DistributionRuleId,
            distributionRunId: request.DistributionRunId,
            isPromotionalRepost: request.IsPromotionalRepost);

        var targetUrl = _urlBuilder.BuildAttributedTargetUrl(
            property.Id,
            publication.UtmSource,
            publication.UtmMedium,
            publication.UtmCampaign,
            publication.UtmContent);

        var imageUrl = ResolveImageUrl(request.ImageUrl, property)
            ?? throw new ConflictException("لا يمكن إنشاء منشور توزيع لعقار بلا صورة متاحة (لا يوجد رابط صورة بديل مُهيأ حالياً).");

        var title = string.IsNullOrWhiteSpace(request.Title) ? property.Title : request.Title!;

        string body;
        IEnumerable<string>? hashtags = request.Hashtags;

        if (string.IsNullOrWhiteSpace(request.Body))
        {
            // Phase 8 spec §3: an explicit Title/Body always wins (manual editorial control) —
            // the generator only ever fills in what the caller left blank, exactly like
            // BuildDefaultBody did before this phase.
            var (generatedBody, generatedHashtags) = await TryGenerateBodyAsync(
                property, account.Platform, targetUrl, request.Language, ct);
            body = generatedBody;
            hashtags ??= generatedHashtags;
        }
        else
        {
            body = request.Body!;
        }

        var content = SocialPostContent.Create(
            publication.Id,
            account.Platform,
            title,
            body,
            imageUrl,
            targetUrl,
            hashtags,
            request.Language);

        publication.AttachContent(content);

        await _publications.AddAsync(publication, ct);

        await _history.AddAsync(
            SocialPublicationStatusHistory.Record(
                publication.Id,
                fromStatus: null,
                toStatus: publication.Status,
                changedByUserId: request.CreatedByUserId,
                note: "تم إنشاء المنشور كمسودة.",
                utcNow: DateTime.UtcNow),
            ct);

        await _uow.SaveChangesAsync(ct);

        return SocialDistributionMapper.ToDto(publication);
    }

    /// <summary>
    /// Phase 8 pipeline: build <see cref="PropertySocialFacts"/> strictly from Domain/DTO data →
    /// call <see cref="ISocialContentGenerator"/> → run <see cref="SocialContentFactValidator"/>
    /// on the result → use it only if valid, otherwise fall back to the pre-existing minimal
    /// fact-line template (spec §3: "AI Output → Fact Validator → Valid → Continue / Invalid →
    /// Reject / Regenerate / Fallback Template"). Never throws — a generator failure/timeout or a
    /// failed validation must never block creating the publication itself, only degrade its copy.
    /// </summary>
    private async Task<(string Body, IReadOnlyList<string> Hashtags)> TryGenerateBodyAsync(
        Property property, Domain.SocialDistribution.Enums.SocialPlatform platform, string targetUrl, string language, CancellationToken ct)
    {
        var fallback = BuildDefaultBody(property);

        try
        {
            var facts = BuildFacts(property, targetUrl);
            var request = new GenerateSocialContentRequest(facts, platform, language);
            var generated = await _contentGenerator.GenerateAsync(request, ct);

            var validation = SocialContentFactValidator.Validate(generated, facts);
            if (!validation.IsValid)
            {
                _logger?.LogWarning(
                    "تم رفض محتوى مولّد للعقار {PropertyId} بسبب: {Errors}. سيتم استخدام القالب الاحتياطي.",
                    property.Id, string.Join("; ", validation.Errors));
                return (fallback, Array.Empty<string>());
            }

            return (generated.Body, generated.Hashtags);
        }
        catch (Exception ex)
        {
            // A generator failure/timeout must never block property distribution (spec §3:
            // "تعامل مع فشل AI باستخدام Template-based Fallback").
            _logger?.LogWarning(ex, "فشل توليد محتوى اجتماعي للعقار {PropertyId}. سيتم استخدام القالب الاحتياطي.", property.Id);
            return (fallback, Array.Empty<string>());
        }
    }

    /// <summary>
    /// The only place Property fields are copied into <see cref="PropertySocialFacts"/> — never
    /// widen this beyond what a generator is allowed to see/use (spec §3). NOTE: PropertyType/
    /// Governorate navigation properties are not <c>.Include()</c>d by
    /// <see cref="IPropertyRepository.GetPublishedByIdWithDetailsAsync"/>, so PropertyType/Province
    /// stay null here — the same documented limitation as Phase 7's asset generator.
    /// </summary>
    private static PropertySocialFacts BuildFacts(Property property, string canonicalUrl) => new(
        PropertyId: property.Id,
        Title: property.Title,
        PropertyType: property.PropertyType?.NameAr,
        TransactionType: property.ListingType.ToString(),
        Province: property.Governorate?.NameAr ?? property.Region,
        City: property.City,
        Address: null,
        Price: property.PurchasePrice ?? property.ColdRent ?? property.WarmRent,
        Currency: property.CurrencyCode,
        Area: property.Area,
        Rooms: property.Rooms,
        Bathrooms: null,
        Status: property.Status.ToString(),
        CanonicalUrl: canonicalUrl);

    /// <summary>An explicit ImageUrl wins; otherwise the property's main image, then its first image.</summary>
    private static string? ResolveImageUrl(string? explicitImageUrl, Property property)
    {
        if (!string.IsNullOrWhiteSpace(explicitImageUrl))
            return explicitImageUrl;

        var main = property.Images.FirstOrDefault(image => image.IsMain);
        if (main is not null && !string.IsNullOrWhiteSpace(main.Url))
            return main.Url;

        var first = property.Images.FirstOrDefault(image => !string.IsNullOrWhiteSpace(image.Url));
        return first?.Url;
    }

    /// <summary>A simple, safe default caption built from structured property facts — mirrors the frontend's PropertyShareContentService.buildMetaDescription, kept independent per bounded-context isolation.</summary>
    private static string BuildDefaultBody(Property property)
    {
        var facts = new List<string>();

        if (!string.IsNullOrWhiteSpace(property.City))
            facts.Add(property.City);

        if (property.Area is > 0)
            facts.Add(string.Format(CultureInfo.InvariantCulture, "{0} م²", property.Area));

        if (property.Rooms is > 0)
            facts.Add(string.Format(CultureInfo.InvariantCulture, "{0} غرف", property.Rooms));

        var price = property.PurchasePrice ?? property.ColdRent ?? property.WarmRent;
        if (price is > 0)
            facts.Add(string.Format(CultureInfo.InvariantCulture, "{0} {1}", price, property.CurrencyCode));

        return facts.Count == 0 ? property.Title : string.Join(" - ", facts);
    }
}
