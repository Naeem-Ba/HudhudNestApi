using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PropertyApi.Application.SocialDistribution.Interfaces;
using PropertyApi.Application.SocialDistribution.Services;
using PropertyApi.Infrastructure.Repositories.SocialDistribution;
using PropertyApi.Infrastructure.SocialDistribution.Assets;
using PropertyApi.Infrastructure.SocialDistribution.Publishing;

namespace PropertyApi.Infrastructure.SocialDistribution;

/// <summary>
/// DI wiring for the SocialDistribution bounded context — a dedicated registration file (rather
/// than folded into RepositoryInfrastructureRegistration) because it also wires the dispatch
/// worker and the ISocialPublisher registry, following the same one-file-per-feature-area
/// pattern as AuthInfrastructureRegistration/MediaInfrastructureRegistration.
/// </summary>
public static class SocialDistributionInfrastructureRegistration
{
    public static IServiceCollection AddSocialDistributionInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ISocialChannelRepository, SocialChannelRepository>();
        services.AddScoped<ISocialAccountRepository, SocialAccountRepository>();
        services.AddScoped<ISocialPublicationRepository, SocialPublicationRepository>();
        services.AddScoped<ISocialPublicationStatusHistoryRepository, SocialPublicationStatusHistoryRepository>();

        // Provinces & Distribution Rules (Phase 4)
        services.AddScoped<IDistributionRuleRepository, DistributionRuleRepository>();
        services.AddScoped<IDistributionRunRepository, DistributionRunRepository>();

        services.AddScoped<ISocialDistributionTargetUrlBuilder, SocialDistributionTargetUrlBuilder>();

        // Phase 5: one concrete ISocialPublisher adapter per supported platform — every one of
        // them still a safe placeholder today (see PlatformNotConfiguredPublisherBase), but each
        // a genuinely separate, independently-registrable class rather than one generic type
        // parameterized by an enum. Adding a new platform (e.g. a real
        // FacebookGraphApiSocialPublisher, or a brand-new platform entirely) is exactly: add its
        // class here — no other registration, and no change to SocialPublisherRegistry, the
        // Distribution Engine, or the worker.
        services.AddSingleton<ISocialPublisher, FacebookPublisher>();
        services.AddSingleton<ISocialPublisher, InstagramPublisher>();
        services.AddSingleton<ISocialPublisher, TelegramPublisher>();
        services.AddSingleton<ISocialPublisher, TikTokPublisher>();
        services.AddSingleton<ISocialPublisher, YouTubePublisher>();
        services.AddSingleton<ISocialPublisher, LinkedInPublisher>();
        services.AddSingleton<ISocialPublisherRegistry, SocialPublisherRegistry>();

        // Phase 6: Queue Port — production adapter wraps the existing SocialPublication
        // Queued/Retrying rows (see SocialPublicationJobQueue's remarks for why no separate Job
        // table exists). tests/.../InMemorySocialPublicationJobQueue is the swappable Test Double.
        services.AddScoped<ISocialPublicationJobQueue, SocialPublicationJobQueue>();
        services.AddScoped<ISocialPublicationDeadLetterRepository, SocialPublicationDeadLetterRepository>();
        services.AddOptions<PropertyApi.Application.SocialDistribution.Options.SocialDistributionRetryOptions>()
            .Bind(configuration.GetSection(PropertyApi.Application.SocialDistribution.Options.SocialDistributionRetryOptions.SectionName));

        // Phase 7: Social Media Asset Generation
        services.AddOptions<BrandOptions>().Bind(configuration.GetSection(BrandOptions.SectionName));
        services.AddScoped<IBrandIdentityProvider, ConfiguredBrandIdentityProvider>();
        services.AddScoped<ISocialMediaAssetRepository, SocialMediaAssetRepository>();
        services.AddScoped<ISocialMediaAssetStorage, MediaSocialMediaAssetStorage>();
        services.AddScoped<ISocialMediaAssetGenerator, SocialMediaAssetGenerator>();

        services.AddHostedService<SocialPublicationDispatchHostedService>();

        return services;
    }
}
