using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Logging;

namespace PropertyApi.Infrastructure.Email;

public sealed class ConsoleEmailSender : IEmailSender
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
    {
        _logger.LogInformation(
            "Development email to {Email}. Subject: {Subject}. Body: {Body}",
            email,
            subject,
            htmlMessage);

        return Task.CompletedTask;
    }
}