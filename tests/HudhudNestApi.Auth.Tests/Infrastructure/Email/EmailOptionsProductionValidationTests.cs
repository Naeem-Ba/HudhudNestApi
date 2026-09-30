using HudhudNestApi.Infrastructure.Email;

namespace HudhudNestApi.Auth.Tests.Infrastructure.Email;

/// <summary>
/// Email:From defaults to the shipped placeholder no-reply@hudhudnest.local, a
/// non-routable domain. Every other startup check passes with that value -- it is
/// non-empty, and nothing in this process can ask Resend whether a domain is verified --
/// so Resend rejects the domain with a 403 at send time, and both callers of the send path
/// swallow that failure by design. This is the one deployment mistake ValidateForEnvironment
/// exists to catch before the process ever accepts traffic.
/// </summary>
public sealed class EmailOptionsProductionValidationTests
{
    [Fact(DisplayName = "Production rejects the shipped placeholder From domain")]
    public void ValidateForEnvironment_ShouldThrow_WhenFromIsThePlaceholderDomainInProduction()
    {
        var options = new EmailOptions { From = "no-reply@hudhudnest.local" };

        var exception = Assert.Throws<InvalidOperationException>(
            () => options.ValidateForEnvironment("Production"));

        Assert.Contains("hudhudnest.local", exception.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Production accepts a verified sending domain")]
    public void ValidateForEnvironment_ShouldNotThrow_WhenFromIsARealDomainInProduction()
    {
        var options = new EmailOptions { From = "no-reply@example.com" };

        var exception = Record.Exception(() => options.ValidateForEnvironment("Production"));

        Assert.Null(exception);
    }

    [Fact(DisplayName = "Development allows the placeholder From domain")]
    public void ValidateForEnvironment_ShouldNotThrow_WhenFromIsThePlaceholderDomainOutsideProduction()
    {
        var options = new EmailOptions { From = "no-reply@hudhudnest.local" };

        var exception = Record.Exception(() => options.ValidateForEnvironment("Development"));

        Assert.Null(exception);
    }
}
