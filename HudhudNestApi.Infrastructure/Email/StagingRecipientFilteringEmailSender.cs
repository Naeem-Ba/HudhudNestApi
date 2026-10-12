using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Common.Models;
using IdentityEmailSender = Microsoft.AspNetCore.Identity.UI.Services.IEmailSender;

namespace HudhudNestApi.Infrastructure.Email;

/// <summary>
/// Staging's guarantee that it never mails a real person. StagingEnvironmentGuard insists on
/// <c>Staging:ExternalNotificationsDisabled=true</c>, but nothing consulted that flag when a message
/// was sent, so whether Staging reached real inboxes depended entirely on which provider and API key
/// happened to be configured there. This wrapper makes the flag mean what it says: a message is
/// handed to the real sender only when its recipient is on <c>Staging:Email:AllowedRecipients</c>
/// (exact addresses, or <c>@domain</c> entries for a whole domain, case-insensitive). With no list
/// configured nothing is delivered. Suppressed messages are counted in the log without the address.
/// </summary>
public sealed class StagingRecipientFilteringEmailSender
    : IdentityEmailSender, IApplicationEmailSender
{
    private readonly IApplicationEmailSender _inner;
    private readonly HashSet<string> _addresses;
    private readonly string[] _domainSuffixes;
    private readonly ILogger<StagingRecipientFilteringEmailSender> _logger;

    public StagingRecipientFilteringEmailSender(
        IApplicationEmailSender inner,
        IEnumerable<string> allowedRecipients,
        ILogger<StagingRecipientFilteringEmailSender> logger)
    {
        _inner = inner;
        _logger = logger;

        var entries = allowedRecipients
            .Select(entry => entry?.Trim())
            .Where(entry => !string.IsNullOrEmpty(entry))
            .Select(entry => entry!)
            .ToArray();

        _addresses = new HashSet<string>(
            entries.Where(entry => !entry.StartsWith('@')),
            StringComparer.OrdinalIgnoreCase);
        _domainSuffixes = entries
            .Where(entry => entry.StartsWith('@') && entry.Length > 1)
            .ToArray();
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
        if (IsAllowed(message.To))
            return _inner.SendEmailAsync(message, ct);

        // Never log the address or the body: a suppressed message may still carry a real person's
        // data, and the point of this class is that Staging stays away from real people.
        _logger.LogInformation(
            "Staging suppressed an outgoing email (recipient is not on Staging:Email:AllowedRecipients). Subject: {Subject}.",
            message.Subject);

        return Task.CompletedTask;
    }

    private bool IsAllowed(string? recipient)
    {
        var address = recipient?.Trim();
        if (string.IsNullOrEmpty(address))
            return false;

        if (_addresses.Contains(address))
            return true;

        return _domainSuffixes.Any(suffix =>
            address.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }
}
