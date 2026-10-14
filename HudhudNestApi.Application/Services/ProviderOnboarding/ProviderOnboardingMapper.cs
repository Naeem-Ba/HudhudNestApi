using HudhudNestApi.Domain.Services;
using HudhudNestApi.Domain.Services.Entities;

namespace HudhudNestApi.Application.Services.ProviderOnboarding;

public static class ProviderOnboardingMapper
{
    public static ServiceProviderEventDto ToDto(ServiceProviderEvent e, bool forApplicant) => new(
        e.Id,
        e.Type,
        e.Note,
        e.IsInternal,
        forApplicant ? null : e.ActorUserId,
        e.ApplicationId,
        e.CreatedAt);

    public static ServiceProviderApplicationDto ToApplicantDto(
        ServiceProviderApplication a, IEnumerable<ServiceProviderEvent> events) => new(
        a.Id,
        a.ReferenceNumber,
        a.ApplicantType,
        a.DisplayName,
        a.ContactName,
        a.ContactEmail,
        a.ContactPhone,
        ServiceCategorySet.FromNames(a.RequestedCategories),
        a.RequestedGovernorateIds,
        a.Experience,
        a.CompanyName,
        a.LicenseNumber,
        a.Status,
        a.InfoRequest,
        a.DecisionReason,
        a.ServiceProviderId,
        a.CreatedAt,
        a.UpdatedAt,
        // Server-side filter: the applicant never receives an internal row, whatever the caller asked.
        events.Where(e => !e.IsInternal).Select(e => ToDto(e, forApplicant: true)).ToList());

    public static AdminServiceProviderApplicationSummaryDto ToSummary(ServiceProviderApplication a) => new(
        a.Id,
        a.ReferenceNumber,
        a.ApplicantUserId,
        a.ApplicantType,
        a.DisplayName,
        a.ContactName,
        a.ContactEmail,
        a.ContactPhone,
        ServiceCategorySet.FromNames(a.RequestedCategories),
        a.RequestedGovernorateIds,
        a.Status,
        a.CreatedAt,
        a.UpdatedAt);

    public static AdminServiceProviderSummaryDto ToSummary(ServiceProvider p) => new(
        p.Id,
        p.UserId,
        p.DisplayName,
        p.ContactEmail,
        p.ContactPhone,
        p.Status,
        p.VerificationLevel,
        ServiceCategorySet.FromNames(p.ApprovedCategories),
        p.CoverageGovernorateIds,
        p.CreatedAt,
        p.UpdatedAt);
}
