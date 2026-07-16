using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PropertyApi.Application.Common.Interfaces;

namespace PropertyApi.Infrastructure.Media;

internal static class MediaInfrastructureRegistration
{
    private const string CloudinaryUrlKey = "CLOUDINARY_URL";

    public static IServiceCollection AddMediaInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<CloudinaryOptions>()
            .Configure(options =>
                ConfigureCloudinaryOptions(configuration, options))
            .Validate(
                options =>
                    !string.IsNullOrWhiteSpace(options.CloudName) &&
                    !string.IsNullOrWhiteSpace(options.ApiKey) &&
                    !string.IsNullOrWhiteSpace(options.ApiSecret),
                "Cloudinary credentials are required. Configure either " +
                "CLOUDINARY_URL or Cloudinary:CloudName, " +
                "Cloudinary:ApiKey and Cloudinary:ApiSecret.")
            .ValidateOnStart();

        services.AddHttpClient<CloudinaryMediaStorageService>();

        services.AddScoped<IMediaStorageService>(serviceProvider =>
            serviceProvider.GetRequiredService<CloudinaryMediaStorageService>());

        return services;
    }

    private static void ConfigureCloudinaryOptions(
        IConfiguration configuration,
        CloudinaryOptions options)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);

        /*
         * First load the separate configuration values.
         *
         * Supported sources include:
         * - appsettings.json
         * - appsettings.Production.json
         * - User Secrets
         * - Environment variables:
         *   Cloudinary__CloudName
         *   Cloudinary__ApiKey
         *   Cloudinary__ApiSecret
         */
        configuration
            .GetSection(CloudinaryOptions.SectionName)
            .Bind(options);

        /*
         * CLOUDINARY_URL has priority when it is present.
         *
         * Expected format:
         * cloudinary://API_KEY:API_SECRET@CLOUD_NAME
         */
        var rawCloudinaryUrl = configuration[CloudinaryUrlKey];

        if (string.IsNullOrWhiteSpace(rawCloudinaryUrl))
        {
            return;
        }

        ApplyCloudinaryUrl(rawCloudinaryUrl, options);
    }

    private static void ApplyCloudinaryUrl(
        string rawCloudinaryUrl,
        CloudinaryOptions options)
    {
        var normalizedUrl = rawCloudinaryUrl
            .Trim()
            .Trim('"', '\'');

        if (!Uri.TryCreate(
                normalizedUrl,
                UriKind.Absolute,
                out var cloudinaryUri))
        {
            throw CreateInvalidCloudinaryUrlException();
        }

        if (!string.Equals(
                cloudinaryUri.Scheme,
                "cloudinary",
                StringComparison.OrdinalIgnoreCase))
        {
            throw CreateInvalidCloudinaryUrlException();
        }

        if (string.IsNullOrWhiteSpace(cloudinaryUri.Host))
        {
            throw CreateInvalidCloudinaryUrlException();
        }

        var credentials = cloudinaryUri.UserInfo.Split(
            ':',
            count: 2,
            StringSplitOptions.None);

        if (credentials.Length != 2)
        {
            throw CreateInvalidCloudinaryUrlException();
        }

        var apiKey = Uri.UnescapeDataString(credentials[0]).Trim();
        var apiSecret = Uri.UnescapeDataString(credentials[1]).Trim();
        var cloudName = Uri.UnescapeDataString(cloudinaryUri.Host).Trim();

        if (string.IsNullOrWhiteSpace(apiKey) ||
            string.IsNullOrWhiteSpace(apiSecret) ||
            string.IsNullOrWhiteSpace(cloudName))
        {
            throw CreateInvalidCloudinaryUrlException();
        }

        /*
         * Overwrite all three values together.
         *
         * This prevents credentials from CLOUDINARY_URL being mixed
         * with credentials from the separate configuration fields.
         */
        options.CloudName = cloudName;
        options.ApiKey = apiKey;
        options.ApiSecret = apiSecret;
    }

    private static InvalidOperationException CreateInvalidCloudinaryUrlException()
    {
        return new InvalidOperationException(
            "CLOUDINARY_URL is invalid. Expected format: " +
            "cloudinary://API_KEY:API_SECRET@CLOUD_NAME. " +
            "Special characters in the API key or API secret must be URL-encoded.");
    }
}