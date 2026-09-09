using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.SocialDistribution.Interfaces;
using PropertyApi.Domain.SocialDistribution.Enums;
using PropertyApi.Infrastructure.Repositories.SocialDistribution;
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
    public static IServiceCollection AddSocialDistributionInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<ISocialChannelRepository, SocialChannelRepository>();
        services.AddScoped<ISocialAccountRepository, SocialAccountRepository>();
        services.AddScoped<ISocialPublicationRepository, SocialPublicationRepository>();
        services.AddScoped<ISocialPublicationStatusHistoryRepository, SocialPublicationStatusHistoryRepository>();

        // Provinces & Distribution Rules (Phase 4)
        services.AddScoped<IDistributionRuleRepository, DistributionRuleRepository>();
        services.AddScoped<IDistributionRunRepository, DistributionRunRepository>();

        services.AddScoped<ISocialDistributionTargetUrlBuilder, SocialDistributionTargetUrlBuilder>();

        // One ISocialPublisher per supported platform — every one of them a StubSocialPublisher
        // today (spec §11/§14: no official platform integration exists yet; see its remarks for
        // how a real one plugs in later). Registered as a collection so
        // PublishSocialPublicationCommandHandler's IEnumerable<ISocialPublisher> constructor
        // dependency resolves them all and picks by Platform at call time.
        foreach (var platform in Enum.GetValues<SocialPlatform>())
        {
            services.AddSingleton<ISocialPublisher>(sp =>
                new StubSocialPublisher(platform, sp.GetRequiredService<ILogger<StubSocialPublisher>>()));
        }

        services.AddHostedService<SocialPublicationDispatchHostedService>();

        return services;
    }
}
