using System.Net;
using System.Net.Mail;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PropertyApi.Infrastructure.Email;

public sealed class SmtpEmailSender : IEmailSender
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

    public async Task SendEmailAsync(
        string email,
        string subject,
        string htmlMessage)
    {
        if (string.IsNullOrWhiteSpace(_options.SmtpHost))
        {
            _logger.LogError("SMTP host is not configured.");
            throw new InvalidOperationException("SMTP host is not configured.");
        }

        var from = string.IsNullOrWhiteSpace(_options.From)
            ? _options.Username
            : _options.From;

        using var message = new MailMessage
        {
            From = new MailAddress(from),
            Subject = subject,
            Body = htmlMessage,
            IsBodyHtml = true
        };

        message.To.Add(email);

        using var client = new SmtpClient(_options.SmtpHost, _options.SmtpPort)
        {
            EnableSsl = _options.EnableSsl,
            Credentials = new NetworkCredential(
                _options.Username,
                _options.Password)
        };

        await client.SendMailAsync(message);
    }
}