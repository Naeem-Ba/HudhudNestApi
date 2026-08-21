using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PropertyApi.Application.Admin.Interfaces;
using PropertyApi.Application.Analytics.Interfaces;
using PropertyApi.Application.Bookings.Interfaces;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Contact.Interfaces;
using PropertyApi.Application.Favorites.Interfaces;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Application.Notifications.Interfaces;
using PropertyApi.Application.Reviews.Interfaces;
using PropertyApi.Application.Search.Interfaces;
using PropertyApi.Application.Users.Interfaces;
using PropertyApi.Application.Users.Messaging.Interfaces;
using PropertyApi.Infrastructure.Admin;
using PropertyApi.Infrastructure.Analytics;
using PropertyApi.Infrastructure.Bookings;
using PropertyApi.Infrastructure.Notifications;
using PropertyApi.Infrastructure.Reviews;
using PropertyApi.Infrastructure.Search;

namespace PropertyApi.Infrastructure.Repositories;

internal static class RepositoryInfrastructureRegistration
{
    public static IServiceCollection AddRepositoryInfrastructure(
        this IServiceCollection services)
    {
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
        services.AddScoped<IPropertyImageRepository, PropertyImageRepository>();
        services.AddScoped<IFavoriteRepository, FavoriteRepository>();
        services.AddScoped<IContactMessageRepository, ContactMessageRepository>();
        services.AddScoped<IUserDirectoryReadService, UserDirectoryReadService>();
        services.AddScoped<IUserAccountRepository, UserAccountRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();

        return services;
    }
}
