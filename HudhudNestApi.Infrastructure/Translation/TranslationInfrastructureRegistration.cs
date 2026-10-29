using HudhudNestApi.Application.Listings.Translations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
                new DeepLTranslationProvider(sp.GetRequiredService<IHttpClientFactory>(), deepL));
        }
        else
        {
            services.AddSingleton<ITranslationProvider, NoTranslationProvider>();
        }

        return services;
    }
}
