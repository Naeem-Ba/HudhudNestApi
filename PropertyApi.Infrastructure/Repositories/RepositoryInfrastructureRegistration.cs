using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PropertyApi.Application.Admin.Interfaces;
using PropertyApi.Application.Agencies.Interfaces;
using PropertyApi.Application.Analytics.Interfaces;
using PropertyApi.Application.Bookings.Interfaces;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Contact.Interfaces;
using PropertyApi.Application.Favorites.Interfaces;
using PropertyApi.Application.Investments.Interfaces;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Application.Notifications.Interfaces;
using PropertyApi.Application.Plans.Interfaces;
using PropertyApi.Application.Reviews.Interfaces;
using PropertyApi.Application.Search.Interfaces;
using PropertyApi.Application.Services.Interfaces;
using PropertyApi.Application.Users.Interfaces;
using PropertyApi.Application.Users.Messaging.Interfaces;
using PropertyApi.Application.ShortStay.Interfaces;
using PropertyApi.Application.ShortStay.Services;
using PropertyApi.Infrastructure.Admin;
using PropertyApi.Infrastructure.Analytics;
using PropertyApi.Infrastructure.Bookings;
using PropertyApi.Infrastructure.Investments;
using PropertyApi.Infrastructure.Listings;
using PropertyApi.Infrastructure.Notifications;
using PropertyApi.Infrastructure.Reviews;
using PropertyApi.Infrastructure.Search;
using PropertyApi.Infrastructure.Services;
using PropertyApi.Infrastructure.ShortStay;

namespace PropertyApi.Infrastructure.Repositories;

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
        services.AddScoped<IUserDirectoryReadService, UserDirectoryReadService>();
        services.AddScoped<IUserAccountRepository, UserAccountRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddScoped<IPlanRepository, PlanRepository>();

        // AqarTech Services Marketplace
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

        // Investment Discovery module (Phase 1)
        services.AddScoped<IInvestmentProjectRepository, InvestmentProjectRepository>();
        services.AddScoped<IInvestmentProjectFinancialsRepository, InvestmentProjectFinancialsRepository>();
        services.AddScoped<IInvestmentRiskAssessmentRepository, InvestmentRiskAssessmentRepository>();
        services.AddScoped<IInvestmentDocumentRepository, InvestmentDocumentRepository>();
        services.AddScoped<IInvestmentUpdateRepository, InvestmentUpdateRepository>();
        services.AddScoped<IInvestmentWatchlistRepository, InvestmentWatchlistRepository>();
        services.AddScoped<IInvestmentInterestRepository, InvestmentInterestRepository>();

        return services;
    }
}
