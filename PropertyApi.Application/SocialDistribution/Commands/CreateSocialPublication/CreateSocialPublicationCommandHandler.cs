using System.Globalization;
using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Application.SocialDistribution.DTOs;
using PropertyApi.Application.SocialDistribution.Interfaces;
using PropertyApi.Application.SocialDistribution.Mapping;
using PropertyApi.Domain.Listings.Entities;
using PropertyApi.Domain.SocialDistribution.Entities;

namespace PropertyApi.Application.SocialDistribution.Commands.CreateSocialPublication;

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
    private readonly IUnitOfWork _uow;

    public CreateSocialPublicationCommandHandler(
        IPropertyRepository properties,
        ISocialAccountRepository accounts,
        ISocialChannelRepository channels,
        ISocialPublicationRepository publications,
        ISocialPublicationStatusHistoryRepository history,
        ISocialDistributionTargetUrlBuilder urlBuilder,
        IUnitOfWork uow)
    {
        _properties = properties;
        _accounts = accounts;
        _channels = channels;
        _publications = publications;
        _history = history;
        _urlBuilder = urlBuilder;
        _uow = uow;
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
            distributionRunId: request.DistributionRunId);

        var targetUrl = _urlBuilder.BuildAttributedTargetUrl(
            property.Id,
            publication.UtmSource,
            publication.UtmMedium,
            publication.UtmCampaign,
            publication.UtmContent);

        var imageUrl = ResolveImageUrl(request.ImageUrl, property)
            ?? throw new ConflictException("لا يمكن إنشاء منشور توزيع لعقار بلا صورة متاحة (لا يوجد رابط صورة بديل مُهيأ حالياً).");

        var title = string.IsNullOrWhiteSpace(request.Title) ? property.Title : request.Title!;
        var body = string.IsNullOrWhiteSpace(request.Body) ? BuildDefaultBody(property) : request.Body!;

        var content = SocialPostContent.Create(
            publication.Id,
            account.Platform,
            title,
            body,
            imageUrl,
            targetUrl,
            request.Hashtags,
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
