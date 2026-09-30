using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Infrastructure.Auth.Services;

namespace HudhudNestApi.Infrastructure.Media;

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
            .PostConfigure(options =>
                ApplyCloudinaryUrlFallback(
                    options,
                    configuration["CLOUDINARY_URL"]))
            .PostConfigure(options =>
                options.FolderPrefix = ResolveFolderPrefix(options.FolderPrefix, environment))
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

    // Staging shares Production's Cloudinary cloud on the free plan (credential-only isolation),
    // so without a folder root Staging uploads land in the same folders as real user media.
    // Defaulting here (instead of demanding a new env var) means the isolation is in force on
    // the very next deploy with no dashboard step that could be forgotten.
    internal static string? ResolveFolderPrefix(string? configured, IHostEnvironment environment)
    {
        var prefix = configured?.Trim().Trim('/');
        if (!string.IsNullOrEmpty(prefix))
            return prefix;

        return environment.IsStaging() ? "staging" : null;
    }

    private static void ApplyCloudinaryUrlFallback(
        CloudinaryOptions options,
        string? cloudinaryUrl)
    {
        if (!string.IsNullOrWhiteSpace(options.CloudName) &&
            !string.IsNullOrWhiteSpace(options.ApiKey) &&
            !string.IsNullOrWhiteSpace(options.ApiSecret))
        {
            return;
        }

        if (!Uri.TryCreate(cloudinaryUrl, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, "cloudinary", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            return;
        }

        var separatorIndex = uri.UserInfo.IndexOf(':');
        if (separatorIndex <= 0 || separatorIndex == uri.UserInfo.Length - 1)
        {
            return;
        }

        options.CloudName = Uri.UnescapeDataString(uri.Host);
        options.ApiKey = Uri.UnescapeDataString(uri.UserInfo[..separatorIndex]);
        options.ApiSecret = Uri.UnescapeDataString(uri.UserInfo[(separatorIndex + 1)..]);
    }
}
