using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq.Protected;
using HudhudNestApi.Application.Common.Models;
using HudhudNestApi.Infrastructure.Email;

namespace HudhudNestApi.Auth.Tests.Infrastructure.Email;

/// <summary>
/// The Resend transport, exercised against a stubbed handler.
///
/// The one assertion here that is not about the happy path is about secrecy: the API key
/// lives in a request header and must never reach a log line or an exception message,
/// both of which are routinely pasted into tickets.
/// </summary>
public sealed class ResendEmailSenderTests
{
    private const string ApiKey = "resend-test-secret-must-not-leak";

    [Fact]
    public async Task SendEmailAsync_PostsTheExpectedRequest()
    {
        var context = CreateSender(
            HttpStatusCode.OK,
            """{"id":"49a3999c-0ce1-4ea6-ab68-afcd6dc2e794"}""");

        await context.Sender.SendEmailAsync(
            new EmailMessage(
                "someone@example.com",
                "Confirm your email",
                "<p>link</p>",
                "link"),
            CancellationToken.None);

        Assert.Equal(HttpMethod.Post, context.Method);
        Assert.Equal("https://api.resend.test/emails", context.RequestUri);
        Assert.Equal($"Bearer {ApiKey}", context.Authorization);

        using var payload = JsonDocument.Parse(context.Body!);
        var root = payload.RootElement;

        Assert.Equal("HudhudNestApi <no-reply@example.com>", root.GetProperty("from").GetString());
        Assert.Equal("someone@example.com", root.GetProperty("to")[0].GetString());
        Assert.Equal("Confirm your email", root.GetProperty("subject").GetString());
        Assert.Equal("<p>link</p>", root.GetProperty("html").GetString());
        Assert.Equal("link", root.GetProperty("text").GetString());
    }

    [Fact]
    public async Task SendEmailAsync_OmitsTheTextPart_WhenThereIsNone()
    {
        var context = CreateSender(HttpStatusCode.OK, """{"id":"x"}""");

        // The three-argument overload is the one ASP.NET Identity calls; it has no text
        // part to offer, and a null one is worth leaving out of the payload entirely.
        await context.Sender.SendEmailAsync(
            "someone@example.com",
            "Subject",
            "<p>html</p>");

        using var payload = JsonDocument.Parse(context.Body!);

        Assert.False(payload.RootElement.TryGetProperty("text", out _));
    }

    [Fact]
    public async Task SendEmailAsync_Throws_WhenResendRejectsTheMessage()
    {
        var context = CreateSender(
            HttpStatusCode.UnprocessableEntity,
            """{"statusCode":422,"name":"validation_error","message":"The domain is not verified."}""");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.Sender.SendEmailAsync(
                new EmailMessage("someone@example.com", "Subject", "<p>html</p>"),
                CancellationToken.None));

        // Resend's own wording is what tells "domain not verified" apart from "you may
        // only send to your own address", so it has to survive into the message.
        Assert.Contains("422", exception.Message, StringComparison.Ordinal);
        Assert.Contains("The domain is not verified.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendEmailAsync_NeverPutsTheApiKeyInTheFailureMessage()
    {
        var context = CreateSender(
            HttpStatusCode.Forbidden,
            """{"name":"invalid_api_key","message":"API key is invalid"}""");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.Sender.SendEmailAsync(
                new EmailMessage("someone@example.com", "Subject", "<p>html</p>"),
                CancellationToken.None));

        Assert.DoesNotContain(ApiKey, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendEmailAsync_Throws_WhenTheApiKeyIsMissing()
    {
        var context = CreateSender(
            HttpStatusCode.OK,
            """{"id":"x"}""",
            apiKey: string.Empty);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.Sender.SendEmailAsync(
                new EmailMessage("someone@example.com", "Subject", "<p>html</p>"),
                CancellationToken.None));

        Assert.Contains("Email:Resend:ApiKey", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendEmailAsync_UsesTheBareAddress_WhenNoDisplayNameIsConfigured()
    {
        var context = CreateSender(
            HttpStatusCode.OK,
            """{"id":"x"}""",
            fromName: string.Empty);

        await context.Sender.SendEmailAsync(
            new EmailMessage("someone@example.com", "Subject", "<p>html</p>"),
            CancellationToken.None);

        using var payload = JsonDocument.Parse(context.Body!);

        Assert.Equal(
            "no-reply@example.com",
            payload.RootElement.GetProperty("from").GetString());
    }

    private static SenderContext CreateSender(
        HttpStatusCode statusCode,
        string responseBody,
        string apiKey = ApiKey,
        string fromName = "HudhudNestApi")
    {
        var context = new SenderContext();
        var handler = new Mock<HttpMessageHandler>();

        handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) =>
            {
                // Read inside the callback: HttpClient disposes the request content once
                // the send completes, so the body is unreadable by the time a test looks.
                context.Method = request.Method;
                context.RequestUri = request.RequestUri?.ToString();
                context.Authorization = request.Headers.Authorization?.ToString();
                context.Body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            })
            .ReturnsAsync(() => new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody)
            });

        var httpClient = new HttpClient(handler.Object)
        {
            BaseAddress = new Uri("https://api.resend.test/")
        };

        context.Sender = new ResendEmailSender(
            httpClient,
            Options.Create(new EmailOptions
            {
                Provider = "Resend",
                From = "no-reply@example.com",
                FromName = fromName
            }),
            Options.Create(new ResendEmailOptions
            {
                ApiKey = apiKey,
                BaseUrl = "https://api.resend.test"
            }),
            NullLogger<ResendEmailSender>.Instance);

        return context;
    }

    private sealed class SenderContext
    {
        public ResendEmailSender Sender { get; set; } = null!;

        public HttpMethod? Method { get; set; }

        public string? RequestUri { get; set; }

        public string? Authorization { get; set; }

        public string? Body { get; set; }
    }
}
