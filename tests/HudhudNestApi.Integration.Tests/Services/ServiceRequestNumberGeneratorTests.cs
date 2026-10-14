using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using HudhudNestApi.Application.Services.Interfaces;
using HudhudNestApi.Integration.Tests.Investments;

namespace HudhudNestApi.Integration.Tests.Services;

/// <summary>
/// QA Phase 6: POST /api/ServiceRequests answered 500 on every call because the number generator's raw
/// SELECT nextval(...) did not name its column "Value", which EF Core's SqlQueryRaw requires for a scalar.
/// The handler tests mock the generator, so only a real Postgres run can see it.
/// </summary>
public sealed class ServiceRequestNumberGeneratorTests : IAsyncLifetime
{
    private readonly InvestmentApiTestFactory _factory = new();

    public async Task InitializeAsync() => await _factory.PrepareDatabaseAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact(DisplayName = "The generator hands out increasing SR-<year>-<number> values on real Postgres")]
    public async Task NextAsync_ReturnsIncreasingNumbers()
    {
        var (first, second) = await _factory.InScopeAsync(async services =>
        {
            var generator = services.GetRequiredService<IServiceRequestNumberGenerator>();
            return (await generator.NextAsync(), await generator.NextAsync());
        });

        var pattern = new Regex($@"^SR-{DateTime.UtcNow.Year}-(\d{{6,}})$");
        var a = pattern.Match(first);
        var b = pattern.Match(second);
        Assert.True(a.Success, first);
        Assert.True(b.Success, second);
        Assert.True(long.Parse(b.Groups[1].Value) > long.Parse(a.Groups[1].Value));
    }
}
