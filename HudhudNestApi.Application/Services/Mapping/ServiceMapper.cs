using HudhudNestApi.Application.Services.DTOs;
using HudhudNestApi.Application.Services.Interfaces;
using HudhudNestApi.Domain.Services.Entities;

namespace HudhudNestApi.Application.Services.Mapping;

/// <summary>Who is looking at a service request DTO.</summary>
public enum ServiceRequestAudience { Provider, Requester, Admin }

/// <summary>
/// Entity → DTO projection for the Services bounded context, in one place so the marketplace
/// listing, provider inbox and "my requests" views cannot drift into showing different fields
/// for the same entity. Mirrors AgencyMapper.
/// </summary>
public static class ServiceMapper
{
    public static ServiceProviderDto ToDto(
        ServiceProvider provider, double? averageRating, int reviewCount)
        => new(
            Id: provider.Id,
            UserId: provider.UserId,
            AgencyId: provider.AgencyId,
            DisplayName: provider.DisplayName,
            Bio: provider.Bio,
            LogoUrl: provider.LogoUrl,
            ContactEmail: provider.ContactEmail,
            ContactPhone: provider.ContactPhone,
            VerificationLevel: provider.VerificationLevel,
            IsActive: provider.IsActive,
            AverageRating: averageRating,
            ReviewCount: reviewCount,
            CreatedAt: provider.CreatedAt,
            Status: provider.Status,
            StatusReason: provider.StatusReason,
            ApprovedCategories: Domain.Services.ServiceCategorySet.FromNames(provider.ApprovedCategories),
            CoverageGovernorateIds: provider.CoverageGovernorateIds);

    public static ServiceOfferingDto ToDto(ServiceOffering offering)
        => ToDto(offering, rating: null);

    public static ServiceOfferingDto ToDto(ServiceOffering offering, (double? Average, int Count)? rating)
        => new(
            Id: offering.Id,
            ServiceProviderId: offering.ServiceProviderId,
            ServiceProviderDisplayName: offering.ServiceProvider?.DisplayName ?? "-",
            ServiceProviderVerificationLevel:
                offering.ServiceProvider?.VerificationLevel
                    ?? Domain.Services.Enums.ServiceProviderVerificationLevel.None,
            Category: offering.Category,
            Title: offering.Title,
            Description: offering.Description,
            BasePrice: offering.BasePrice,
            CurrencyId: offering.CurrencyId,
            EstimatedDurationDays: offering.EstimatedDurationDays,
            IsActive: offering.IsActive,
            CreatedAt: offering.CreatedAt,
            ProviderCoverageGovernorateIds: offering.ServiceProvider?.CoverageGovernorateIds ?? [],
            AverageRating: rating?.Average,
            ReviewCount: rating?.Count ?? 0);

    /// <summary>
    /// The audience decides what is shown. The provider's phone/email are an introduction the
    /// requester earned once the provider accepted their confirmed request, so only the requester
    /// (and an admin) get them; the customer's own feedback and reason likewise stay between the customer and the
    /// admin, never in the provider's inbox.
    /// </summary>
    public static ServiceRequestDto ToDto(
        ServiceRequest request,
        ServiceRequestAudience audience = ServiceRequestAudience.Provider,
        ServiceRequestMessageStats? messages = null)
    {
        var includeProviderContact = audience != ServiceRequestAudience.Provider;
        var includeFeedback = audience != ServiceRequestAudience.Provider;
        return new(
            Id: request.Id,
            RequestNumber: request.RequestNumber,
            PropertyId: request.PropertyId,
            PropertyTitle: request.Property?.Title ?? "-",
            PropertyMainImageUrl: request.Property?.Images.FirstOrDefault(i => i.IsMain)?.Url,
            RequesterId: request.RequesterId,
            RequesterName: string.IsNullOrWhiteSpace(request.Requester?.DisplayName)
                ? $"{request.Requester?.FirstName} {request.Requester?.LastName}".Trim()
                : request.Requester!.DisplayName!,
            ServiceProviderId: request.ServiceProviderId,
            ServiceProviderDisplayName: request.ServiceProvider?.DisplayName ?? "-",
            ServiceOfferingId: request.ServiceOfferingId,
            ServiceOfferingTitle: request.ServiceOffering?.Title ?? "-",
            Category: request.Category,
            Status: request.Status,
            RequesterNote: request.RequesterNote,
            ProviderNote: request.ProviderNote,
            ScheduledAt: request.ScheduledAt,
            QuotedPrice: request.QuotedPrice,
            QuotedPriceCurrencyId: request.QuotedPriceCurrencyId,
            FinalPrice: request.FinalPrice,
            FinalPriceCurrencyId: request.FinalPriceCurrencyId,
            RejectionReason: request.RejectionReason,
            CancellationReason: request.CancellationReason,
            CompletedAt: request.CompletedAt,
            CreatedAt: request.CreatedAt,
            UpdatedAt: request.UpdatedAt,
            ProviderContact: includeProviderContact && request.ProviderContactVisible
                ? ContactOf(request.ServiceProvider)
                : null,
            OutcomeReason: request.OutcomeReason,
            OutcomeNote: request.OutcomeNote,
            NotCompletedAt: request.NotCompletedAt,
            RequesterAnswer: includeFeedback ? request.RequesterAnswer : null,
            RequesterReason: includeFeedback ? request.RequesterReason : null,
            RequesterFeedbackNote: includeFeedback ? request.RequesterFeedbackNote : null,
            RequesterFeedbackAt: includeFeedback ? request.RequesterFeedbackAt : null,
            Intent: request.Intent,
            ConfirmedAt: request.ConfirmedAt,
            LastActivityAt: request.LastActivityAt,
            ExpiredAt: request.ExpiredAt,
            ExpiredWaitingOn: request.ExpiredWaitingOn,
            MessageCount: messages?.Total ?? 0,
            UnreadMessageCount: messages?.Unread ?? 0,
            LastMessageAt: messages?.LastAt);
    }

    private static ServiceProviderContactDto? ContactOf(ServiceProvider? provider)
    {
        var phone = string.IsNullOrWhiteSpace(provider?.ContactPhone) ? null : provider!.ContactPhone;
        var email = string.IsNullOrWhiteSpace(provider?.ContactEmail) ? null : provider!.ContactEmail;
        return phone is null && email is null ? null : new ServiceProviderContactDto(phone, email);
    }

    public static ServiceRequestStatusHistoryDto ToDto(ServiceRequestStatusHistory entry)
        => new(
            Id: entry.Id,
            FromStatus: entry.FromStatus,
            ToStatus: entry.ToStatus,
            ChangedByUserId: entry.ChangedByUserId,
            Note: entry.Note,
            CreatedAt: entry.CreatedAt);

    public static ServiceRequestDocumentDto ToDto(ServiceRequestDocument document)
        => new(
            Id: document.Id,
            ServiceRequestId: document.ServiceRequestId,
            FileUrl: document.FileUrl,
            FileType: document.FileType,
            FileName: document.FileName,
            UploadedByUserId: document.UploadedByUserId,
            CreatedAt: document.CreatedAt);

    public static ServiceReviewDto ToDto(ServiceReview review, string reviewerName)
        => new(
            Id: review.Id,
            ServiceRequestId: review.ServiceRequestId,
            ServiceProviderId: review.ServiceProviderId,
            ReviewerId: review.ReviewerId,
            ReviewerName: reviewerName,
            Rating: review.Rating,
            Comment: review.Comment,
            CreatedAt: review.CreatedAt);
}
