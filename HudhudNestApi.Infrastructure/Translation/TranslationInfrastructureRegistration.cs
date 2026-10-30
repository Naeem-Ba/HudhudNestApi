using HudhudNestApi.Application.Listings.Translations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HudhudNestApi.Infrastructure.Translation;

public static class TranslationInfrastructureRegistration
{
    public static IServiceCollection AddListingTranslationInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ListingTranslationOptions>()
            .Bind(configuration.GetSection(ListingTranslationOptions.SectionName));

        services.AddScoped<IListingTranslationStore, ListingTranslationStore>();

        var providerName = configuration[$"{ListingTranslationOptions.SectionName}:Provider"];
        if (string.Equals(providerName, "DeepL", StringComparison.OrdinalIgnoreCase))
        {
            var deepL = new DeepLOptions();
            configuration.GetSection(DeepLOptions.SectionName).Bind(deepL);

            // Same remedy as the Telegram adapter: default HttpClient logging would print every request URI.
            services.AddHttpClient(DeepLTranslationProvider.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(30))
                .RemoveAllLoggers();
            services.AddSingleton<ITranslationProvider>(sp =>
            {
                var provider = new DeepLTranslationProvider(sp.GetRequiredService<IHttpClientFactory>(), deepL);
                if (!provider.IsAvailable)
                    sp.GetRequiredService<ILoggerFactory>().CreateLogger("ListingTranslation")
                        .LogWarning("ListingTranslation:Provider is DeepL but ListingTranslation:DeepL:ApiKey is empty; no listing will be translated until the key is set.");
                return provider;
            });
        }
        else
        {
            services.AddSingleton<ITranslationProvider, NoTranslationProvider>();
        }

        return services;
    }
}
