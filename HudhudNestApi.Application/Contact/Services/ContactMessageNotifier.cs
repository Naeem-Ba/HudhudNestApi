using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Contact.Interfaces;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Application.Notifications.Localization;
using HudhudNestApi.Domain.Messaging.Entities;

namespace HudhudNestApi.Application.Contact.Services;

/// <summary>
/// QA Phase 12 (P12-D10): a contact-form message used to wait in the admin list until somebody opened it. This tells
/// the admins a new one arrived (sender name and subject; the text itself stays in the list). Called after the message
/// is committed and never throws: a notice that fails is logged and the message stays saved.
/// </summary>
public sealed class ContactMessageNotifier
{
    private readonly INotificationService _notifications;
    private readonly IContactMessageRepository _messages;
    private readonly ILogger<ContactMessageNotifier> _logger;

    public ContactMessageNotifier(
        INotificationService notifications,
        IContactMessageRepository messages,
        ILogger<ContactMessageNotifier> logger)
    {
        _notifications = notifications;
        _messages = messages;
        _logger = logger;
    }

    public async Task NotifyAdminsAsync(ContactMessage message, CancellationToken ct)
    {
        try
        {
            var adminIds = await _messages.ListAdminIdsAsync(ct);
            if (adminIds.Count == 0)
                return;

            var text = NotificationText.Of(NotificationKeys.ContactMessageReceived,
                ("name", NotificationText.FromLiteral(message.Name)),
                ("subject", string.IsNullOrWhiteSpace(message.Subject)
                    ? NotificationText.Of(NotificationKeys.ContactMessageNoSubject)
                    : NotificationText.FromLiteral(message.Subject)));

            var dedupeKey = $"cmr:{message.Id:N}";

            foreach (var adminId in adminIds)
            {
                try
                {
                    await _notifications.NotifyContactMessageAsync(adminId, text, message.Id, dedupeKey, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex,
                        "Contact message notification failed. ContactMessageId={ContactMessageId}, AdminId={AdminId}",
                        message.Id, adminId);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Could not notify admins of a new contact message. ContactMessageId={ContactMessageId}", message.Id);
        }
    }
}
