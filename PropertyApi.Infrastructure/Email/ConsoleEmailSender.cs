using Microsoft.Extensions.Logging;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Common.Models;
using IdentityEmailSender = Microsoft.AspNetCore.Identity.UI.Services.IEmailSender;

namespace PropertyApi.Infrastructure.Email;

public sealed class ConsoleEmailSender
    : IdentityEmailSender, IApplicationEmailSender
{
    private readonly ILogger<ConsoleEmailSender> _logger;

    public ConsoleEmailSender(ILogger<ConsoleEmailSender> logger)
    {
        _logger = logger;
    }

    public Task SendEmailAsync(
        string email,
        string subject,
        string htmlMessage)
        => SendEmailAsync(
            new EmailMessage(email, subject, htmlMessage),
            CancellationToken.None);

    public Task SendEmailAsync(
        EmailMessage message,
        CancellationToken ct = default)
    {
        // The plain-text part is logged too: it is the rendering a developer can actually
        // read in a console, and it is where the confirmation link is legible without
        // digging it out of an anchor tag.
        _logger.LogInformation(
            "Development email to {Email}. Subject: {Subject}. Text: {Text}. Body: {Body}",
            message.To,
            message.Subject,
            message.Text ?? "(none)",
            message.Html);

        return Task.CompletedTask;
    }
}
