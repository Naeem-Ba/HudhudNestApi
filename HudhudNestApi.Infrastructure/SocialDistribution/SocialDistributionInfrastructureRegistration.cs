using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using HudhudNestApi.Application.SocialDistribution.Interfaces;
using HudhudNestApi.Application.SocialDistribution.Services;
using HudhudNestApi.Infrastructure.Repositories.SocialDistribution;
using HudhudNestApi.Infrastructure.SocialDistribution.Assets;
using HudhudNestApi.Infrastructure.SocialDistribution.Publishing;

namespace HudhudNestApi.Infrastructure.SocialDistribution;

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

        // Phase 2: the first REAL platform integration — registered AFTER the placeholder above,
        // last-registration-wins, and only when a bot token is actually configured. Without one,
        // Telegram distribution keeps today's exact behavior (deterministic PlatformNotConfigured,
        // zero network calls) instead of every attempt burning a real HTTP call that can only ever
        // fail with a misconfiguration error — same "off until deliberately configured" posture as
        // every other new Phase 1/2 option in this file.
        var telegramBotToken = configuration[$"{TelegramBotOptions.SectionName}:BotToken"];
        if (!string.IsNullOrWhiteSpace(telegramBotToken))
        {
            // The Bot API forces the token into the URL path (/bot<TOKEN>/method), and the default
            // IHttpClientFactory handlers log that full URI at Information level (redacting only
            // the query string) — so without this the token reaches the log stream on every post.
            // Same remedy UnimatrixSmsService already applies; pinned by SocialPublisherHttpLoggingTests.
            services.AddHttpClient(TelegramBotPublisher.HttpClientName).RemoveAllLoggers();
            services.AddSingleton<ISocialPublisher, TelegramBotPublisher>();
        }

        services.AddOptions<TelegramBotOptions>().Bind(configuration.GetSection(TelegramBotOptions.SectionName));

        // Phase 2b: the second real platform integration — same "off until deliberately
        // configured" posture as Telegram above.
        var facebookPageAccessToken = configuration[$"{FacebookGraphApiOptions.SectionName}:PageAccessToken"];
        if (!string.IsNullOrWhiteSpace(facebookPageAccessToken))
        {
            services.AddHttpClient(FacebookGraphApiPublisher.HttpClientName);
            services.AddSingleton<ISocialPublisher, FacebookGraphApiPublisher>();
        }

        services.AddOptions<FacebookGraphApiOptions>().Bind(configuration.GetSection(FacebookGraphApiOptions.SectionName));

        // Phase 2c: the third real platform integration — same "off until deliberately
        // configured" posture as Telegram/Facebook above.
        var instagramAccessToken = configuration[$"{InstagramGraphApiOptions.SectionName}:AccessToken"];
        if (!string.IsNullOrWhiteSpace(instagramAccessToken))
        {
            services.AddHttpClient(InstagramGraphApiPublisher.HttpClientName);
            services.AddSingleton<ISocialPublisher, InstagramGraphApiPublisher>();
        }

        services.AddOptions<InstagramGraphApiOptions>().Bind(configuration.GetSection(InstagramGraphApiOptions.SectionName));

        services.AddSingleton<ISocialPublisherRegistry, SocialPublisherRegistry>();

        // Phase 6: Queue Port — production adapter wraps the existing SocialPublication
        // Queued/Retrying rows (see SocialPublicationJobQueue's remarks for why no separate Job
        // table exists). tests/.../InMemorySocialPublicationJobQueue is the swappable Test Double.
        services.AddScoped<ISocialPublicationJobQueue, SocialPublicationJobQueue>();
        services.AddScoped<ISocialPublicationDeadLetterRepository, SocialPublicationDeadLetterRepository>();
        services.AddOptions<HudhudNestApi.Application.SocialDistribution.Options.SocialDistributionRetryOptions>()
            .Bind(configuration.GetSection(HudhudNestApi.Application.SocialDistribution.Options.SocialDistributionRetryOptions.SectionName));
        services.AddOptions<HudhudNestApi.Application.SocialDistribution.Options.SocialDistributionAssetGenerationOptions>()
            .Bind(configuration.GetSection(HudhudNestApi.Application.SocialDistribution.Options.SocialDistributionAssetGenerationOptions.SectionName));
        services.AddOptions<HudhudNestApi.Application.SocialDistribution.Options.SocialDistributionEligibilityOptions>()
            .Bind(configuration.GetSection(HudhudNestApi.Application.SocialDistribution.Options.SocialDistributionEligibilityOptions.SectionName));
        services.AddOptions<HudhudNestApi.Application.SocialDistribution.Options.SocialDistributionContentReviewOptions>()
            .Bind(configuration.GetSection(HudhudNestApi.Application.SocialDistribution.Options.SocialDistributionContentReviewOptions.SectionName));

        services.AddOptions<HudhudNestApi.Application.SocialDistribution.Options.SocialDistributionReconciliationOptions>()
            .Bind(configuration.GetSection(HudhudNestApi.Application.SocialDistribution.Options.SocialDistributionReconciliationOptions.SectionName));

        // Kill-switch (SocialDistribution:Enabled, default true) — see SocialDistributionSwitchOptions.
        services.AddOptions<HudhudNestApi.Application.SocialDistribution.Options.SocialDistributionSwitchOptions>()
            .Bind(configuration.GetSection(HudhudNestApi.Application.SocialDistribution.Options.SocialDistributionSwitchOptions.SectionName));
        services.AddSingleton<ISocialDistributionSwitch, ConfiguredSocialDistributionSwitch>();

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
