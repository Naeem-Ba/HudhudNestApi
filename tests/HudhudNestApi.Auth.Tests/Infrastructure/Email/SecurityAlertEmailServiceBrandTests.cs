using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Identity.UI.Services;
using HudhudNestApi.Infrastructure.Email;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace HudhudNestApi.Auth.Tests.Infrastructure.Email;

/// <summary>Customer-facing security emails must name HudhudNest's own contact addresses.</summary>
public sealed class SecurityAlertEmailServiceBrandTests
{
    private static (SecurityAlertEmailService Service, List<string> Bodies) Create()
    {
        var bodies = new List<string>();
        var sender = new Mock<IEmailSender>();
        sender.Setup(s => s.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string>((_, _, body) => bodies.Add(body))
            .Returns(Task.CompletedTask);
        return (new SecurityAlertEmailService(sender.Object, NullLogger<SecurityAlertEmailService>.Instance), bodies);
    }

    [Fact]
    public async Task PasswordChangeRequest_NamesHudhudNestSecurityContact()
    {
        var (service, bodies) = Create();
        await service.SendPasswordChangeRequestAsync("u@example.com", "U", "https://hudhudnest.com/reset", 4, "1.2.3.4");
        Assert.Contains("security@hudhudnest.com", Assert.Single(bodies));
    }

    [Fact]
    public async Task AccountLocked_NamesHudhudNestSupportContact()
    {
        var (service, bodies) = Create();
        await service.SendAccountLockedAlertAsync("u@example.com", "U", DateTime.UtcNow.AddMinutes(15), "1.2.3.4");
        Assert.Contains("support@hudhudnest.com", Assert.Single(bodies));
    }

    [Fact]
    public async Task PasswordChanged_NamesHudhudNestSupportContact()
    {
        var (service, bodies) = Create();
        await service.SendPasswordChangedConfirmationAsync("u@example.com", "U");
        Assert.Contains("support@hudhudnest.com", Assert.Single(bodies));
    }

    [Fact]
    public async Task DistributedCacheHealthCheck_ReportsHealthyOnWorkingCache()
    {
        using var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var distributed = new Microsoft.Extensions.Caching.Distributed.MemoryDistributedCache(
            Microsoft.Extensions.Options.Options.Create(new Microsoft.Extensions.Caching.Memory.MemoryDistributedCacheOptions()));
        var check = new HudhudNestApi.Infrastructure.Health.DistributedCacheHealthCheck(distributed);
        var result = await check.CheckHealthAsync(new Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext());
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy, result.Status);
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("Development")]
    public void RedisCache_UsesHudhudNestKeyPrefix(string environmentName)
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Redis"] = "localhost:6379" }).Build();
        var env = new Mock<Microsoft.Extensions.Hosting.IHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns(environmentName);
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        HudhudNestApi.Infrastructure.Caching.CacheInfrastructureRegistration.AddCacheInfrastructure(services, config, env.Object);
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.Extensions.Caching.StackExchangeRedis.RedisCacheOptions>>().Value;
        Assert.Equal("HudhudNestApi:", options.InstanceName);
    }
}
