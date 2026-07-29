using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Infrastructure.Auth.Services;

namespace PropertyApi.Infrastructure.Media;

internal static class MediaInfrastructureRegistration
{
    public static IServiceCollection AddMediaInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        if (StagingTestSupportPolicy.IsEnabled(environment, configuration) &&
            configuration.GetValue<bool>("Staging:TestSupport:UseInMemoryMedia"))
        {
            services.AddSingleton<StagingSmokeMediaStorageService>();
            services.AddSingleton<IMediaStorageService>(sp =>
                sp.GetRequiredService<StagingSmokeMediaStorageService>());
            return services;
        }

        services.AddOptions<CloudinaryOptions>()
            .Bind(configuration.GetSection(CloudinaryOptions.SectionName))
            .Validate(
                options =>
                    !string.IsNullOrWhiteSpace(options.CloudName) &&
                    !string.IsNullOrWhiteSpace(options.ApiKey) &&
                    !string.IsNullOrWhiteSpace(options.ApiSecret),
                "Cloudinary:CloudName, Cloudinary:ApiKey and Cloudinary:ApiSecret are required.")
            .ValidateOnStart();

        services.AddHttpClient<CloudinaryMediaStorageService>();
        services.AddScoped<IMediaStorageService>(sp =>
            sp.GetRequiredService<CloudinaryMediaStorageService>());

        return services;
    }
}
