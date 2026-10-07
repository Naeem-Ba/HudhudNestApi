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
        var options = new EmailOptions { Provider = "Resend", From = "no-reply@example.com" };

        var exception = Record.Exception(() => options.ValidateForEnvironment("Production"));

        Assert.Null(exception);
    }

    // Production ran with Email:Provider unset (so SMTP) and no Email:SmtpHost: every
    // registration returned 201 while the confirmation mail failed with "SMTP host is not
    // configured", a failure both callers swallow. The process now refuses to start instead.
    [Theory(DisplayName = "Production refuses SMTP without a host")]
    [InlineData("")]
    [InlineData("Smtp")]
    [InlineData("smtp")]
    [InlineData("  SMTP ")]
    public void ValidateForEnvironment_ShouldThrow_WhenProviderIsSmtpWithoutAHostInProduction(string provider)
    {
        var options = new EmailOptions { Provider = provider, From = "no-reply@example.com" };

        var exception = Assert.Throws<InvalidOperationException>(
            () => options.ValidateForEnvironment("Production"));

        Assert.Contains("Email:SmtpHost", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Resend", exception.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Production accepts SMTP once a host is configured")]
    public void ValidateForEnvironment_ShouldNotThrow_WhenProviderIsSmtpWithAHostInProduction()
    {
        var options = new EmailOptions
        {
            Provider = "Smtp",
            From = "no-reply@example.com",
            SmtpHost = "smtp.example.com"
        };

        var exception = Record.Exception(() => options.ValidateForEnvironment("Production"));

        Assert.Null(exception);
    }

    [Fact(DisplayName = "Production does not ask Resend for an SMTP host")]
    public void ValidateForEnvironment_ShouldNotRequireASmtpHost_WhenProviderIsResend()
    {
        var options = new EmailOptions { Provider = "Resend", From = "no-reply@example.com" };

        var exception = Record.Exception(() => options.ValidateForEnvironment("Production"));

        Assert.Null(exception);
    }

    [Fact(DisplayName = "Outside Production a blank provider is not an error")]
    public void ValidateForEnvironment_ShouldNotThrow_WhenSmtpHostIsMissingOutsideProduction()
    {
        var options = new EmailOptions { From = "no-reply@example.com" };

        var exception = Record.Exception(() => options.ValidateForEnvironment("Staging"));

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
