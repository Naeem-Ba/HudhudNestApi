using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PropertyApi.Infrastructure.Auth.Services;

namespace PropertyApi.Infrastructure.Health;

public sealed class ProductionStartupValidator : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<ProductionStartupValidator> _logger;

    public ProductionStartupValidator(
        IServiceProvider services,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<ProductionStartupValidator> logger)
    {
        _services = services;
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_environment.IsProduction())
        {
            return;
        }

        ValidateProductionDatabaseConnection();
        ValidateSmsSettings();
        await ValidatePostGisAsync(cancellationToken);

        _logger.LogInformation("Production startup validation completed successfully.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void ValidateProductionDatabaseConnection()
    {
        var rawConnectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? _configuration.GetConnectionString("DefaultConnection")
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(rawConnectionString))
        {
            throw new InvalidOperationException(
                "Production database connection is missing. Configure DATABASE_URL or ConnectionStrings:DefaultConnection.");
        }

        if (rawConnectionString.Contains("Trust Server Certificate=true", StringComparison.OrdinalIgnoreCase) ||
            rawConnectionString.Contains("TrustServerCertificate=true", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Production database connection must not use Trust Server Certificate=true. Use a trusted CA certificate with SSL Mode=Require or VerifyFull.");
        }
    }

    private void ValidateSmsSettings()
    {
        using var scope = _services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<SmsProviderOptions>>().Value;
        options.ValidateForEnvironment(_environment.EnvironmentName);
    }

    private async Task ValidatePostGisAsync(CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();
        var healthCheck = scope.ServiceProvider.GetRequiredService<PostGisHealthCheck>();
        var result = await healthCheck.CheckHealthAsync(
            new HealthCheckContext
            {
                Registration = new HealthCheckRegistration(
                    "postgis-startup",
                    _ => healthCheck,
                    HealthStatus.Unhealthy,
                    tags: null)
            },
            cancellationToken);

        if (result.Status != HealthStatus.Healthy)
        {
            throw new InvalidOperationException(
                $"PostGIS startup validation failed: {result.Description}",
                result.Exception);
        }
    }
}
