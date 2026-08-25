using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Common.Models;
using IdentityEmailSender = Microsoft.AspNetCore.Identity.UI.Services.IEmailSender;

namespace PropertyApi.Infrastructure.Email;

public sealed class SmtpEmailSender
    : IdentityEmailSender, IApplicationEmailSender
{
    private readonly EmailOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(
        IOptions<EmailOptions> options,
        ILogger<SmtpEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task SendEmailAsync(
        string email,
        string subject,
        string htmlMessage)
        => SendEmailAsync(
            new EmailMessage(email, subject, htmlMessage),
            CancellationToken.None);

    public async Task SendEmailAsync(
        EmailMessage message,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.SmtpHost))
        {
            _logger.LogError("SMTP host is not configured.");
            throw new InvalidOperationException("SMTP host is not configured.");
        }

        var from = string.IsNullOrWhiteSpace(_options.From)
            ? _options.Username
            : _options.From;

        using var mail = new MailMessage
        {
            From = string.IsNullOrWhiteSpace(_options.FromName)
                ? new MailAddress(from)
                : new MailAddress(from, _options.FromName),
            Subject = message.Subject
        };

        if (string.IsNullOrWhiteSpace(message.Text))
        {
            mail.Body = message.Html;
            mail.IsBodyHtml = true;
        }
        else
        {
            // multipart/alternative, least-capable rendering first, as RFC 2046 orders it.
            mail.AlternateViews.Add(
                AlternateView.CreateAlternateViewFromString(
                    message.Text,
                    null,
                    MediaTypeNames.Text.Plain));

            mail.AlternateViews.Add(
                AlternateView.CreateAlternateViewFromString(
                    message.Html,
                    null,
                    MediaTypeNames.Text.Html));
        }

        mail.To.Add(message.To);

        using var client = new SmtpClient(
            _options.SmtpHost,
            _options.SmtpPort)
        {
            EnableSsl = _options.EnableSsl,
            Credentials = new NetworkCredential(
                _options.Username,
                _options.Password)
        };

        await client.SendMailAsync(mail, ct);
    }
}
