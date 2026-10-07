using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using HudhudNestApi.Application.Auth.Interfaces;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Common.Models;
using HudhudNestApi.Infrastructure.Email.Templates;
using Xunit;
using IdentityEmailSender = Microsoft.AspNetCore.Identity.UI.Services.IEmailSender;

namespace HudhudNestApi.Integration.Tests.Auth;

/// <summary>
/// Registration by email, the mail it sends, the link in it and the endpoint the link calls,
/// through the real HTTP pipeline and real Postgres, with the mail captured at the sender.
///
/// Production registered accounts and mailed nothing for days: the sender fell back to SMTP
/// with no host, the failure was swallowed (by design), and the app told the user a link had
/// been sent. Nothing exercised this path end to end, and confirming an address shared a
/// three-per-hour rate-limit bucket with password reset.
/// </summary>
[Collection("AuthPostgres")]
public sealed class EmailConfirmationFlowTests
{
    private const string Password = "MyStrong!Pass93x";
    private const string FrontendOrigin = "https://hudhudnest.com";

    private sealed class MailSink : IApplicationEmailSender, IdentityEmailSender
    {
        public ConcurrentQueue<EmailMessage> Sent { get; } = new();

        public bool FailSends { get; set; }

        public Task SendEmailAsync(string email, string subject, string htmlMessage)
            => SendEmailAsync(new EmailMessage(email, subject, htmlMessage), CancellationToken.None);

        public Task SendEmailAsync(EmailMessage message, CancellationToken ct = default)
        {
            if (FailSends)
            {
                throw new InvalidOperationException("simulated mail provider failure");
            }

            Sent.Enqueue(message);
            return Task.CompletedTask;
        }
    }

    private static (WebApplicationFactory<Program> Factory, HttpClient Client, MailSink Mail)
        Start(PostgresAuthTestFactory database)
    {
        var mail = new MailSink();
        var factory = database.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Frontend:BaseUrl"] = FrontendOrigin
                }));

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IApplicationEmailSender>();
                services.RemoveAll<IdentityEmailSender>();
                services.AddSingleton<IApplicationEmailSender>(mail);
                services.AddSingleton<IdentityEmailSender>(mail);
            });
        });

        return (factory, factory.CreateClient(), mail);
    }

    private static object RegisterBody(string email, string? language = null) => new
    {
        firstName = "Naeem",
        lastName = "Test",
        email,
        password = Password,
        privacyPolicyAccepted = true,
        privacyPolicyVersion = "2026-10-01",
        termsAccepted = true,
        termsVersion = "2026-10-01",
        consentSource = "Web",
        language
    };

    private static string UniqueEmail() => $"confirm-flow-{Guid.NewGuid():N}@example.com";

    /// <summary>
    /// The link exactly as the app's router reads it: Angular turns '+' into a space and then
    /// percent-decodes, so a token only survives if the backend encoded '+' as %2B.
    /// </summary>
    private static (Guid UserId, string Token, string Link) ReadLink(EmailMessage message)
    {
        var link = Regex.Match(message.Text!, @"https?://\S+").Value;
        var query = new Uri(link).Query.TrimStart('?')
            .Split('&')
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => pair[0], pair => pair[1]);

        static string AsAngularDecodes(string raw) => Uri.UnescapeDataString(raw.Replace("+", "%20"));

        return (Guid.Parse(AsAngularDecodes(query["userId"])), AsAngularDecodes(query["token"]), link);
    }

    private static async Task<bool> IsConfirmedAsync(WebApplicationFactory<Program> factory, Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var read = scope.ServiceProvider.GetRequiredService<IUserIdentityReadService>();
        return (await read.FindByIdAsync(userId, CancellationToken.None))!.EmailConfirmed;
    }

    [Fact]
    public async Task Register_MailsALinkThatConfirmsTheAddress()
    {
        await using var database = new PostgresAuthTestFactory();
        await database.PrepareDatabaseAsync();
        var (factory, client, mail) = Start(database);
        await using var host = factory;
        var email = UniqueEmail();

        var response = await client.PostAsJsonAsync("/api/auth/register", RegisterBody(email));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("confirmationEmailSent").GetBoolean());

        var message = Assert.Single(mail.Sent);
        Assert.Equal(email, message.To);
        var (userId, token, link) = ReadLink(message);
        Assert.StartsWith($"{FrontendOrigin}/auth/verify-email?userId=", link);
        Assert.Equal(Guid.Parse(body.GetProperty("userId").GetString()!), userId);
        Assert.False(await IsConfirmedAsync(factory, userId));

        var click = await client.PostAsJsonAsync("/api/auth/email/verify", new { userId, token });
        Assert.Equal(HttpStatusCode.OK, click.StatusCode);
        Assert.True(await IsConfirmedAsync(factory, userId));

        var clickAgain = await client.PostAsJsonAsync("/api/auth/email/verify", new { userId, token });
        Assert.Equal(HttpStatusCode.OK, clickAgain.StatusCode);
    }

    [Fact]
    public async Task Verify_RejectsATamperedTokenAndAnotherAccountsToken()
    {
        await using var database = new PostgresAuthTestFactory();
        await database.PrepareDatabaseAsync();
        var (factory, client, mail) = Start(database);
        await using var host = factory;

        await client.PostAsJsonAsync("/api/auth/register", RegisterBody(UniqueEmail()));
        await client.PostAsJsonAsync("/api/auth/register", RegisterBody(UniqueEmail()));
        Assert.True(mail.Sent.TryDequeue(out var firstMail));
        Assert.True(mail.Sent.TryDequeue(out var secondMail));
        var (firstId, firstToken, _) = ReadLink(firstMail!);
        var (secondId, secondToken, _) = ReadLink(secondMail!);

        var tampered = await client.PostAsJsonAsync(
            "/api/auth/email/verify",
            new { userId = secondId, token = secondToken[..^4] + "AAAA" });
        var crossed = await client.PostAsJsonAsync(
            "/api/auth/email/verify",
            new { userId = secondId, token = firstToken });

        Assert.Equal(HttpStatusCode.BadRequest, tampered.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, crossed.StatusCode);
        Assert.False(await IsConfirmedAsync(factory, firstId));
        Assert.False(await IsConfirmedAsync(factory, secondId));
    }

    [Fact]
    public async Task Register_WhenTheProviderFails_StillCreatesTheAccount_AndSaysNoLinkWasSent()
    {
        await using var database = new PostgresAuthTestFactory();
        await database.PrepareDatabaseAsync();
        var (factory, client, mail) = Start(database);
        await using var host = factory;
        mail.FailSends = true;
        var email = UniqueEmail();

        var response = await client.PostAsJsonAsync("/api/auth/register", RegisterBody(email));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("confirmationEmailSent").GetBoolean());
        Assert.Empty(mail.Sent);

        // The recovery the app offers next: once the provider works, resend delivers a link
        // that confirms the address.
        mail.FailSends = false;
        var resend = await client.PostAsJsonAsync("/api/auth/email/resend-confirmation", new { email });
        Assert.Equal(HttpStatusCode.OK, resend.StatusCode);
        var message = Assert.Single(mail.Sent);
        var (userId, token, _) = ReadLink(message);
        var click = await client.PostAsJsonAsync("/api/auth/email/verify", new { userId, token });
        Assert.Equal(HttpStatusCode.OK, click.StatusCode);
        Assert.True(await IsConfirmedAsync(factory, userId));
    }

    [Fact]
    public async Task Resend_AnswersAnUnknownAddressLikeAKnownOne_AndMailsNothing()
    {
        await using var database = new PostgresAuthTestFactory();
        await database.PrepareDatabaseAsync();
        var (factory, client, mail) = Start(database);
        await using var host = factory;

        var response = await client.PostAsJsonAsync(
            "/api/auth/email/resend-confirmation",
            new { email = UniqueEmail() });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(mail.Sent);
    }

    [Fact]
    public async Task ConfirmingAndResending_DoNotShareThePasswordResetAllowance()
    {
        await using var database = new PostgresAuthTestFactory();
        await database.PrepareDatabaseAsync();
        var (factory, client, _) = Start(database);
        await using var host = factory;
        var someone = new { email = UniqueEmail() };

        // Use up the whole password-reset allowance (3 per hour per client).
        for (var i = 0; i < 3; i++)
        {
            var forgot = await client.PostAsJsonAsync("/api/auth/forgot-password", someone);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, forgot.StatusCode);
        }

        var forgotAgain = await client.PostAsJsonAsync("/api/auth/forgot-password", someone);
        Assert.Equal(HttpStatusCode.TooManyRequests, forgotAgain.StatusCode);

        // Clicking a confirmation link and asking for a new one still work: a user who retried
        // a link once used to be told it was invalid.
        for (var i = 0; i < 4; i++)
        {
            var click = await client.PostAsJsonAsync(
                "/api/auth/email/verify",
                new { userId = Guid.NewGuid(), token = "not-a-real-token" });
            Assert.Equal(HttpStatusCode.BadRequest, click.StatusCode);
        }

        var resend = await client.PostAsJsonAsync("/api/auth/email/resend-confirmation", someone);
        Assert.Equal(HttpStatusCode.OK, resend.StatusCode);
    }

    [Fact]
    public async Task Resend_StaysTight_SixthRequestInAnHourIsRefused()
    {
        await using var database = new PostgresAuthTestFactory();
        await database.PrepareDatabaseAsync();
        var (factory, client, _) = Start(database);
        await using var host = factory;
        var someone = new { email = UniqueEmail() };

        for (var i = 0; i < 5; i++)
        {
            var allowed = await client.PostAsJsonAsync("/api/auth/email/resend-confirmation", someone);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        var refused = await client.PostAsJsonAsync("/api/auth/email/resend-confirmation", someone);
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
    }

    [Theory]
    [InlineData("ar", "هدهد نيست", "<html lang=\"ar\" dir=\"rtl\">", "Confirm your email address")]
    [InlineData("en", "HudhudNest", "<html lang=\"en\" dir=\"ltr\">", "أكِّد بريدك الإلكتروني")]
    [InlineData("de", "HudhudNest", "<html lang=\"de\" dir=\"ltr\">", "Confirm your email address")]
    public async Task Register_MailsInTheLanguageTheClientWasShowing(
        string language,
        string brand,
        string htmlTag,
        string otherLanguageHeading)
    {
        await using var database = new PostgresAuthTestFactory();
        await database.PrepareDatabaseAsync();
        var (factory, client, mail) = Start(database);
        await using var host = factory;

        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            RegisterBody(UniqueEmail(), language));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var message = Assert.Single(mail.Sent);
        Assert.Contains(brand, message.Subject, StringComparison.Ordinal);
        Assert.Contains(htmlTag, message.Html, StringComparison.Ordinal);
        Assert.Contains(brand, message.Html, StringComparison.Ordinal);
        Assert.DoesNotContain(otherLanguageHeading, message.Html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("fr")]
    [InlineData("")]
    public async Task Register_WithoutAShippedLanguage_MailsTheBilingualMessage(string? language)
    {
        await using var database = new PostgresAuthTestFactory();
        await database.PrepareDatabaseAsync();
        var (factory, client, mail) = Start(database);
        await using var host = factory;

        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            RegisterBody(UniqueEmail(), language));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var message = Assert.Single(mail.Sent);
        Assert.Equal(EmailConfirmationTemplate.Subject, message.Subject);
        Assert.Contains("أكِّد بريدك الإلكتروني", message.Html, StringComparison.Ordinal);
        Assert.Contains("Confirm your email address", message.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resend_MailsInTheLanguageTheClientWasShowing()
    {
        await using var database = new PostgresAuthTestFactory();
        await database.PrepareDatabaseAsync();
        var (factory, client, mail) = Start(database);
        await using var host = factory;
        var email = UniqueEmail();

        await client.PostAsJsonAsync("/api/auth/register", RegisterBody(email, "ar"));
        Assert.True(mail.Sent.TryDequeue(out _));

        var resend = await client.PostAsJsonAsync(
            "/api/auth/email/resend-confirmation",
            new { email, language = "de" });

        Assert.Equal(HttpStatusCode.OK, resend.StatusCode);
        var message = Assert.Single(mail.Sent);
        Assert.Contains("<html lang=\"de\" dir=\"ltr\">", message.Html, StringComparison.Ordinal);
        Assert.Contains("E-Mail-Adresse bestätigen", message.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Register_WithATakenEmail_AnswersConflict_WithAStableCode()
    {
        await using var database = new PostgresAuthTestFactory();
        await database.PrepareDatabaseAsync();
        var (factory, client, _) = Start(database);
        await using var host = factory;
        var email = UniqueEmail();

        var first = await client.PostAsJsonAsync("/api/auth/register", RegisterBody(email, "ar"));
        var second = await client.PostAsJsonAsync("/api/auth/register", RegisterBody(email, "ar"));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        // The message stays (older app builds print it); the code is what a translated client
        // keys on, because the message is English and the page may not be.
        var body = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("EMAIL_ALREADY_REGISTERED", body.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("message").GetString()));
    }
}
