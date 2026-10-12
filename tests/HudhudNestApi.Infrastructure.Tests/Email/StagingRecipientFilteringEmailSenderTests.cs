using Microsoft.Extensions.Logging.Abstractions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Common.Models;
using HudhudNestApi.Infrastructure.Email;
using IdentityEmailSender = Microsoft.AspNetCore.Identity.UI.Services.IEmailSender;

namespace HudhudNestApi.Infrastructure.Tests.Email;

public sealed class StagingRecipientFilteringEmailSenderTests
{
    [Fact]
    public async Task With_no_allowed_recipients_nothing_is_delivered()
    {
        var inner = new RecordingSender();
        var sender = Create(inner);

        await sender.SendEmailAsync(new EmailMessage("someone@example.com", "Hello", "<p>hi</p>"));
        await sender.SendEmailAsync("other@example.org", "Hello", "<p>hi</p>");

        Assert.Empty(inner.Sent);
    }

    [Theory]
    [InlineData("tester@example.com")]
    [InlineData("TESTER@Example.COM")]
    [InlineData("  tester@example.com  ")]
    public async Task An_exact_allowed_address_is_delivered_case_insensitively(string recipient)
    {
        var inner = new RecordingSender();
        var sender = Create(inner, "tester@example.com");

        await sender.SendEmailAsync(new EmailMessage(recipient, "Hello", "<p>hi</p>"));

        Assert.Single(inner.Sent);
    }

    [Fact]
    public async Task A_domain_entry_allows_every_address_of_that_domain_only()
    {
        var inner = new RecordingSender();
        var sender = Create(inner, "@qa.hudhudnest.test");

        await sender.SendEmailAsync(new EmailMessage("a@qa.hudhudnest.test", "ok", "x"));
        await sender.SendEmailAsync(new EmailMessage("a@QA.hudhudnest.TEST", "ok", "x"));
        await sender.SendEmailAsync(new EmailMessage("a@evilqa.hudhudnest.test", "no", "x"));
        await sender.SendEmailAsync(new EmailMessage("a@qa.hudhudnest.test.evil.example", "no", "x"));
        await sender.SendEmailAsync(new EmailMessage("real.person@gmail.com", "no", "x"));

        Assert.Equal(new[] { "ok", "ok" }, inner.Sent.Select(m => m.Subject).ToArray());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_missing_recipient_is_never_delivered(string? recipient)
    {
        var inner = new RecordingSender();
        var sender = Create(inner, "@qa.hudhudnest.test", "tester@example.com");

        await sender.SendEmailAsync(new EmailMessage(recipient!, "Hello", "x"));

        Assert.Empty(inner.Sent);
    }

    [Fact]
    public async Task Blank_and_bare_at_entries_are_ignored_rather_than_allowing_everything()
    {
        var inner = new RecordingSender();
        var sender = Create(inner, "", "   ", "@");

        await sender.SendEmailAsync(new EmailMessage("someone@example.com", "Hello", "x"));

        Assert.Empty(inner.Sent);
    }

    [Fact]
    public async Task Both_contracts_apply_the_same_filter_and_forward_the_whole_message()
    {
        var inner = new RecordingSender();
        var sender = Create(inner, "tester@example.com");
        IdentityEmailSender identity = sender;

        await identity.SendEmailAsync("tester@example.com", "Identity", "<p>a</p>");
        await identity.SendEmailAsync("stranger@example.com", "Identity", "<p>b</p>");
        await ((IApplicationEmailSender)sender).SendEmailAsync(
            new EmailMessage("tester@example.com", "App", "<p>c</p>", "plain"));

        Assert.Equal(2, inner.Sent.Count);
        Assert.Equal("plain", inner.Sent[1].Text);
    }

    private static StagingRecipientFilteringEmailSender Create(
        IApplicationEmailSender inner,
        params string[] allowed) =>
        new(inner, allowed, NullLogger<StagingRecipientFilteringEmailSender>.Instance);

    private sealed class RecordingSender : IApplicationEmailSender
    {
        public List<EmailMessage> Sent { get; } = new();

        public Task SendEmailAsync(string email, string subject, string htmlMessage)
            => SendEmailAsync(new EmailMessage(email, subject, htmlMessage));

        public Task SendEmailAsync(EmailMessage message, CancellationToken ct = default)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }
}
