using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
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
using Xunit;
using IdentityEmailSender = Microsoft.AspNetCore.Identity.UI.Services.IEmailSender;

namespace HudhudNestApi.Integration.Tests.Auth;

/// <summary>
/// A Google or Apple account is created without a password. The app's phone section tells such an account that
/// confirming a number "needs a password", so what that account can do about it matters: this pins that the
/// emailed "forgot password" link gives a social-only account a password (and nothing else about it changes),
/// which is the one route from "no password" to "can confirm a phone by code".
/// </summary>
[Collection("AuthPostgres")]
public sealed class SocialOnlyAccountPasswordTests
{
    private const string NewPassword = "MyStrong!Pass93x";

    private sealed class MailSink : IApplicationEmailSender, IdentityEmailSender
    {
        public ConcurrentQueue<EmailMessage> Sent { get; } = new();

        public Task SendEmailAsync(string email, string subject, string htmlMessage)
            => SendEmailAsync(new EmailMessage(email, subject, htmlMessage), CancellationToken.None);

        public Task SendEmailAsync(EmailMessage message, CancellationToken ct = default)
        {
            Sent.Enqueue(message);
            return Task.CompletedTask;
        }
    }

    [Fact(DisplayName = "A Google-only account sets a password through the emailed reset link and can then sign in with it")]
    [Trait("Category", "Security")]
    public async Task GoogleOnlyAccount_CanSetAPasswordByEmailReset()
    {
        var email = $"google-only-{Guid.NewGuid():N}@example.test";
        var seed = new SocialIdentitySeed(
            RawToken: $"token-{Guid.NewGuid():N}",
            ProviderName: "Google",
            ProviderId: $"provider-{Guid.NewGuid():N}",
            Email: email,
            IsEmailVerified: true);

        await using var database = new PostgresAuthTestFactory(socialSeeds: new[] { seed });
        await database.PrepareDatabaseAsync();

        var mail = new MailSink();
        await using var factory = database.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Frontend:BaseUrl"] = "https://hudhudnest.com",
                    ["Frontend:PasswordResetUrl"] = "https://hudhudnest.com/auth/reset-password"
                }));

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IApplicationEmailSender>();
                services.RemoveAll<IdentityEmailSender>();
                services.AddSingleton<IApplicationEmailSender>(mail);
                services.AddSingleton<IdentityEmailSender>(mail);
            });
        });
        var client = factory.CreateClient();

        var social = await client.PostAsJsonAsync("/api/auth/social/google", new { idToken = seed.RawToken });
        Assert.Equal(HttpStatusCode.OK, social.StatusCode);
        var access = (await social.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;

        var before = await MeAsync(client, access);
        Assert.False(before.GetProperty("hasPassword").GetBoolean());

        var password = await client.PostAsJsonAsync("/api/auth/login", new { email, password = NewPassword });
        Assert.NotEqual(HttpStatusCode.OK, password.StatusCode);

        var forgot = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        Assert.Equal(HttpStatusCode.OK, forgot.StatusCode);

        var message = Assert.Single(mail.Sent, m => string.Equals(m.To, email, StringComparison.OrdinalIgnoreCase)
            && m.Subject.Contains("password", StringComparison.OrdinalIgnoreCase));
        var link = System.Net.WebUtility.HtmlDecode(
            Regex.Match(message.Text ?? message.Html, @"https?://[^""'<>\s]+").Value);
        var query = new Uri(link).Query.TrimStart('?')
            .Split('&')
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => pair[0], pair => Uri.UnescapeDataString(pair[1].Replace("+", "%20")));

        var reset = await client.PostAsJsonAsync("/api/auth/reset-password", new
        {
            email,
            token = query["token"],
            newPassword = NewPassword,
            confirmPassword = NewPassword
        });
        Assert.True(reset.StatusCode == HttpStatusCode.OK,
            $"reset-password answered {(int)reset.StatusCode}: {await reset.Content.ReadAsStringAsync()}");

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = NewPassword });
        Assert.True(login.StatusCode == HttpStatusCode.OK,
            $"login answered {(int)login.StatusCode}: {await login.Content.ReadAsStringAsync()}");
        var passwordAccess = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;

        var after = await MeAsync(client, passwordAccess);
        Assert.True(after.GetProperty("hasPassword").GetBoolean());
        Assert.Equal(before.GetProperty("id").GetString(), after.GetProperty("id").GetString());
    }

    private static async Task<JsonElement> MeAsync(HttpClient client, string access)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/users/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }
}
