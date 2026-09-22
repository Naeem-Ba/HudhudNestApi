using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using HudhudNestApi.Application.Admin.Interfaces;
using HudhudNestApi.Application.Agencies.Interfaces;
using HudhudNestApi.Application.Analytics.Interfaces;
using HudhudNestApi.Application.AppUpdates.Interfaces;
using HudhudNestApi.Application.Bookings.Interfaces;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Contact.Interfaces;
using HudhudNestApi.Application.Favorites.Interfaces;
using HudhudNestApi.Application.Investments.Interfaces;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Application.Marketing.Interfaces;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Application.Plans.Interfaces;
using HudhudNestApi.Application.Reviews.Interfaces;
using HudhudNestApi.Application.Search.Interfaces;
using HudhudNestApi.Application.Services.Interfaces;
using HudhudNestApi.Application.Users.Interfaces;
using HudhudNestApi.Application.Users.Messaging.Interfaces;
using HudhudNestApi.Application.ShortStay.Interfaces;
using HudhudNestApi.Application.ShortStay.Services;
using HudhudNestApi.Application.Valuation.Interfaces;
using HudhudNestApi.Infrastructure.Admin;
using HudhudNestApi.Infrastructure.Analytics;
using HudhudNestApi.Infrastructure.AppUpdates;
using HudhudNestApi.Infrastructure.Bookings;
using HudhudNestApi.Infrastructure.Investments;
using HudhudNestApi.Infrastructure.Listings;
using HudhudNestApi.Infrastructure.Notifications;
using HudhudNestApi.Infrastructure.Reviews;
using HudhudNestApi.Infrastructure.Search;
using HudhudNestApi.Infrastructure.Services;
using HudhudNestApi.Infrastructure.ShortStay;
using HudhudNestApi.Infrastructure.Valuation;

namespace HudhudNestApi.Infrastructure.Repositories;

internal static class RepositoryInfrastructureRegistration
{
    public static IServiceCollection AddRepositoryInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Plan-driven (BACKEND-ISSUES.md §B-3) — depends on the scoped IPlanRepository and
        // IAgencyRepository below, so this must be Scoped too, not Singleton (a singleton
        // capturing a scoped dependency is a DI captive-dependency bug).
        services.AddScoped<IListingQuotaPolicy, ListingQuotaPolicy>();

        // Sums Property + ShortStayListing (the listing types that draw from the shared plan
        // quota) — depends on IPropertyRepository/IShortStayListingRepository/IAgencyRepository
        // below, so registered after them for readability (order does not affect DI resolution).
        services.AddScoped<IActiveListingCounter, ActiveListingCounter>();

        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IAdminUserQueryRepository, AdminUserQueryRepository>();
        services.AddScoped<IAdminIdentityService, AdminIdentityService>();
        services.AddScoped<IPropertyGeoSearchRepository, PropertyGeoSearchRepository>();
        services.AddScoped<IAnalyticsReadRepository, AnalyticsReadRepository>();
        services.AddScoped<IPropertyReadRepository, PropertyReadRepository>();
        services.AddScoped<IVisitRepository, VisitRepository>();
        services.AddScoped<IPropertyReviewRepository, PropertyReviewRepository>();
        services.AddScoped<IUserRatingRepository, UserRatingRepository>();
        services.AddScoped<IPropertyRepository, PropertyRepository>();
        services.AddScoped<IPropertyPriceHistoryRepository, PropertyPriceHistoryRepository>();
        services.AddScoped<ISavedSearchRepository, SavedSearchRepository>();
        services.AddHostedService<SavedSearchMatchHostedService>();
        services.AddScoped<IListingFeeRepository, ListingFeeRepository>();
        services.AddScoped<IAgencyRepository, AgencyRepository>();
        services.AddScoped<IAgencyInvitationRepository, AgencyInvitationRepository>();
        services.AddHostedService<ListingExpiryHostedService>();
        services.AddScoped<IPropertyImageRepository, PropertyImageRepository>();
        services.AddScoped<IFavoriteRepository, FavoriteRepository>();
        services.AddScoped<IConsentRecordRepository, ConsentRecordRepository>();
        services.AddScoped<IContactMessageRepository, ContactMessageRepository>();
        services.AddScoped<ILeadRepository, LeadRepository>();
        services.AddScoped<IOfferRepository, OfferRepository>();
        services.AddScoped<ISurveyResponseRepository, SurveyResponseRepository>();
        services.AddScoped<IMarketingEventRepository, MarketingEventRepository>();
        services.AddScoped<IPropertyShareEventRepository, PropertyShareEventRepository>();
        services.AddScoped<IPropertyAttributionEventRepository, PropertyAttributionEventRepository>();
        services.AddScoped<IUserDirectoryReadService, UserDirectoryReadService>();
        services.AddScoped<IUserAccountRepository, UserAccountRepository>();
        services.AddScoped<IAccountDataExportRepository, HudhudNestApi.Infrastructure.Users.AccountDataExportRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddScoped<IPlanRepository, PlanRepository>();

        // HudhudNest Services Marketplace
        services.AddScoped<IServiceProviderRepository, ServiceProviderRepository>();
        services.AddScoped<IServiceOfferingRepository, ServiceOfferingRepository>();
        services.AddScoped<IServiceRequestRepository, ServiceRequestRepository>();
        services.AddScoped<IServiceRequestStatusHistoryRepository, ServiceRequestStatusHistoryRepository>();
        services.AddScoped<IServiceReviewRepository, ServiceReviewRepository>();
        services.AddScoped<IServiceRequestDocumentRepository, ServiceRequestDocumentRepository>();
        services.AddScoped<IServiceRequestNumberGenerator, ServiceRequestNumberGenerator>();

        // Short-Stay Accommodation subsystem
        services.AddScoped<IAccommodationTypeRepository, AccommodationTypeRepository>();
        services.AddScoped<IShortStayListingRepository, ShortStayListingRepository>();
        services.AddScoped<IRoomTypeRepository, RoomTypeRepository>();
        services.AddScoped<IAccommodationUnitRepository, AccommodationUnitRepository>();
        services.AddScoped<IPricingRuleRepository, PricingRuleRepository>();
        services.AddScoped<IMinimumStayRuleRepository, MinimumStayRuleRepository>();
        services.AddScoped<IBookingRepository, BookingRepository>();
        services.AddScoped<IShortStayReviewRepository, ShortStayReviewRepository>();
        services.AddScoped<IPricingCalculationService, PricingCalculationService>();

        // App Update Management (Phase 1)
        services.AddScoped<IAppReleaseRepository, AppReleaseRepository>();
        services.AddScoped<IAppReleaseCacheService, CachedAppReleaseCacheService>();

        // Investment Discovery module (Phase 1)
        services.AddScoped<IInvestmentProjectRepository, InvestmentProjectRepository>();
        services.AddScoped<IInvestmentProjectFinancialsRepository, InvestmentProjectFinancialsRepository>();
        services.AddScoped<IInvestmentRiskAssessmentRepository, InvestmentRiskAssessmentRepository>();
        services.AddScoped<IInvestmentDocumentRepository, InvestmentDocumentRepository>();
        services.AddScoped<IInvestmentUpdateRepository, InvestmentUpdateRepository>();
        services.AddScoped<IInvestmentWatchlistRepository, InvestmentWatchlistRepository>();
        services.AddScoped<IInvestmentInterestRepository, InvestmentInterestRepository>();

        // Valuation module (Stage 5 — 24h SLA enforcement; Stage 6 — office dashboard/response)
        services.AddScoped<IValuationInquiryRepository, ValuationInquiryRepository>();
        services.AddScoped<IValuationOfficeInvitationRepository, ValuationOfficeInvitationRepository>();
        services.AddScoped<IValuationOfficeResponseRepository, ValuationOfficeResponseRepository>();
        services.AddScoped<IValuationContactConsentRepository, ValuationContactConsentRepository>();
        services.AddHostedService<ValuationInquiryExpiryHostedService>();

        return services;
    }
}
