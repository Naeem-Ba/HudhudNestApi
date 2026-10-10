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
        var options = new EmailOptions { Provider = "Resend", From = "no-reply@hudhudnest.com" };

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
        var options = new EmailOptions { Provider = provider, From = "no-reply@hudhudnest.com" };

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
            From = "no-reply@hudhudnest.com",
            SmtpHost = "smtp.example.com"
        };

        var exception = Record.Exception(() => options.ValidateForEnvironment("Production"));

        Assert.Null(exception);
    }

    [Fact(DisplayName = "Production does not ask Resend for an SMTP host")]
    public void ValidateForEnvironment_ShouldNotRequireASmtpHost_WhenProviderIsResend()
    {
        var options = new EmailOptions { Provider = "Resend", From = "no-reply@hudhudnest.com" };

        var exception = Record.Exception(() => options.ValidateForEnvironment("Production"));

        Assert.Null(exception);
    }

    // The shipped placeholder is not the only sender that can never work: RFC 2606 / RFC 6761
    // names and example.* domains cannot be verified with a mail provider either, so a sender on
    // one of them is a left-over test value that Resend rejects with a silent 403 at send time.
    [Theory(DisplayName = "Production rejects reserved and test sender domains")]
    [InlineData("no-reply@example.test")]
    [InlineData("no-reply@mail.invalid")]
    [InlineData("no-reply@hudhudnest.example")]
    [InlineData("no-reply@example.com")]
    [InlineData("no-reply@mail.example.org")]
    [InlineData("no-reply@EXAMPLE.NET")]
    [InlineData("no-reply@localhost")]
    [InlineData("no-reply@staging.local")]
    public void ValidateForEnvironment_ShouldThrow_WhenFromIsAReservedDomainInProduction(string from)
    {
        var options = new EmailOptions { Provider = "Resend", From = from };

        var exception = Assert.Throws<InvalidOperationException>(
            () => options.ValidateForEnvironment("Production"));

        Assert.Contains("Email:From", exception.Message, StringComparison.Ordinal);
    }

    [Theory(DisplayName = "Production accepts real sender domains that merely resemble reserved ones")]
    [InlineData("no-reply@hudhudnest.com")]
    [InlineData("no-reply@mail.hudhudnest.com")]
    [InlineData("no-reply@testimony.org")]
    [InlineData("no-reply@myexample.com")]
    public void ValidateForEnvironment_ShouldNotThrow_WhenFromIsARealLookingDomainInProduction(string from)
    {
        var options = new EmailOptions { Provider = "Resend", From = from };

        var exception = Record.Exception(() => options.ValidateForEnvironment("Production"));

        Assert.Null(exception);
    }

    [Theory(DisplayName = "Staging and Development keep accepting reserved sender domains")]
    [InlineData("Staging")]
    [InlineData("Development")]
    public void ValidateForEnvironment_ShouldNotThrow_WhenFromIsAReservedDomainOutsideProduction(string environment)
    {
        var options = new EmailOptions { Provider = "Resend", From = "no-reply@example.test" };

        var exception = Record.Exception(() => options.ValidateForEnvironment(environment));

        Assert.Null(exception);
    }

    [Fact(DisplayName = "Outside Production a blank provider is not an error")]
    public void ValidateForEnvironment_ShouldNotThrow_WhenSmtpHostIsMissingOutsideProduction()
    {
        var options = new EmailOptions { From = "no-reply@hudhudnest.com" };

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
