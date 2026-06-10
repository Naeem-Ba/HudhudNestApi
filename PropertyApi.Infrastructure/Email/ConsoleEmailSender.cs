using Microsoft.Extensions.Logging;
using PropertyApi.Application.Common.Interfaces;
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
    {
        _logger.LogInformation(
            "Development email to {Email}. Subject: {Subject}. Body: {Body}",
            email,
            subject,
            htmlMessage);

        return Task.CompletedTask;
    }
}