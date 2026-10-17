using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Application.Notifications.Localization;
using HudhudNestApi.Application.Services.DTOs;
using HudhudNestApi.Application.Services.Interfaces;
using HudhudNestApi.Application.Services.Mapping;
using HudhudNestApi.Domain.Notifications.Enums;
using HudhudNestApi.Domain.Services;
using HudhudNestApi.Domain.Services.Entities;
using HudhudNestApi.Domain.Services.Enums;

namespace HudhudNestApi.Application.Services.Commands.ServiceConversation;

/// <summary>
/// Which side of a request an actor is, in one place so the send, read and confirm handlers cannot
/// disagree about who may do what. An admin is neither side: read-only on the thread.
/// </summary>
internal static class ServiceRequestParties
{
    public static async Task<(ServiceRequest Request, ServiceProvider Provider, ServiceParty? Party)> ResolveAsync(
        IServiceRequestRepository requests,
        IServiceProviderRepository providers,
        Guid serviceRequestId,
        Guid actorUserId,
        bool isAdmin,
        CancellationToken ct)
    {
        var request = await requests.GetByIdAsync(serviceRequestId, ct)
            ?? throw new NotFoundException("طلب الخدمة غير موجود.");

        var provider = await providers.GetByIdAsync(request.ServiceProviderId, ct)
            ?? throw new NotFoundException("مزوّد الخدمة غير موجود.");

        ServiceParty? party = request.RequesterId == actorUserId
            ? ServiceParty.Requester
            : provider.UserId == actorUserId ? ServiceParty.Provider : null;

        if (party is null && !isAdmin)
            throw new ForbiddenException("لا يمكنك عرض محادثة طلب خدمة لا يخصّك.");

        return (request, provider, party);
    }

    public static string NameOf(Domain.Users.Entities.UserAccount? user)
        => string.IsNullOrWhiteSpace(user?.DisplayName)
            ? $"{user?.FirstName} {user?.LastName}".Trim()
            : user!.DisplayName!;
}

// ─────────────────────────────────────────────────────────────────────────────
// Send a message
// ─────────────────────────────────────────────────────────────────────────────

public sealed record SendServiceRequestMessageCommand(
    Guid ServiceRequestId,
    Guid ActorUserId,
    string Body) : IRequest<ServiceRequestMessageDto>;

public sealed class SendServiceRequestMessageCommandValidator : AbstractValidator<SendServiceRequestMessageCommand>
{
    public SendServiceRequestMessageCommandValidator()
    {
        RuleFor(x => x.Body)
            .NotEmpty().WithMessage("اكتب رسالتك أولًا.")
            .MaximumLength(ServiceRequestLifecyclePolicy.MaxMessageLength)
            .WithMessage($"الرسالة أطول من {ServiceRequestLifecyclePolicy.MaxMessageLength} حرفًا.");
    }
}

public sealed class SendServiceRequestMessageCommandHandler
    : IRequestHandler<SendServiceRequestMessageCommand, ServiceRequestMessageDto>
{
    /// <summary>
    /// Two messages in a row from the same person inside this window notify the other side once:
    /// a burst of short lines must not become a burst of pushes.
    /// </summary>
    private static readonly TimeSpan NotifyBurstWindow = TimeSpan.FromMinutes(10);

    private readonly IServiceRequestRepository _requests;
    private readonly IServiceProviderRepository _providers;
    private readonly IServiceRequestMessageRepository _messages;
    private readonly IUnitOfWork _uow;
    private readonly INotificationService _notifications;
    private readonly ILogger<SendServiceRequestMessageCommandHandler> _logger;

    public SendServiceRequestMessageCommandHandler(
        IServiceRequestRepository requests,
        IServiceProviderRepository providers,
        IServiceRequestMessageRepository messages,
        IUnitOfWork uow,
        INotificationService notifications,
        ILogger<SendServiceRequestMessageCommandHandler> logger)
    {
        _requests = requests;
        _providers = providers;
        _messages = messages;
        _uow = uow;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<ServiceRequestMessageDto> Handle(SendServiceRequestMessageCommand command, CancellationToken ct)
    {
        var (request, provider, party) = await ServiceRequestParties.ResolveAsync(
            _requests, _providers, command.ServiceRequestId, command.ActorUserId, isAdmin: false, ct);

        var sender = party!.Value;
        var now = DateTime.UtcNow;

        if (!request.CanExchangeMessages)
            throw new ConflictException("انتهى هذا الطلب ولا يمكن إرسال رسائل عليه.");

        if (await _messages.CountAsync(request.Id, ct) >= ServiceRequestLifecyclePolicy.MaxMessagesPerRequest)
            throw new ConflictException("وصلت المحادثة إلى الحد الأعلى من الرسائل.");

        var previous = await _messages.GetLastAsync(request.Id, ct);

        var message = ServiceRequestMessage.Create(request.Id, command.ActorUserId, sender, command.Body, now);
        request.RecordMessage(now);

        await _messages.AddAsync(message, ct);
        await _uow.SaveChangesAsync(ct);

        var burst = previous is not null
            && previous.SenderUserId == command.ActorUserId
            && now - previous.CreatedAt < NotifyBurstWindow;

        if (!burst)
        {
            try
            {
                await _notifications.NotifyPropertyUpdateAsync(
                    recipientId: sender == ServiceParty.Requester ? provider.UserId : request.RequesterId,
                    propertyId: request.PropertyId,
                    propertyTitle: request.Property?.Title ?? "-",
                    type: NotificationType.ServiceRequestMessage,
                    detail: NotificationText.Of(NotificationKeys.ServiceMessageDetail, ("number", request.RequestNumber)),
                    relatedEntityId: request.Id,
                    ct: ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to send service-request-message notification. ServiceRequestId={ServiceRequestId}",
                    request.Id);
            }
        }

        var name = sender == ServiceParty.Requester
            ? ServiceRequestParties.NameOf(request.Requester)
            : provider.DisplayName;

        return new ServiceRequestMessageDto(
            message.Id, request.Id, sender, name, message.Body, message.CreatedAt, message.ReadAt, IsMine: true);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Read the thread
// ─────────────────────────────────────────────────────────────────────────────

public sealed record GetServiceRequestMessagesQuery(
    Guid ServiceRequestId,
    Guid ActorUserId,
    bool IsAdmin) : IRequest<IReadOnlyList<ServiceRequestMessageDto>>;

public sealed class GetServiceRequestMessagesQueryHandler
    : IRequestHandler<GetServiceRequestMessagesQuery, IReadOnlyList<ServiceRequestMessageDto>>
{
    private readonly IServiceRequestRepository _requests;
    private readonly IServiceProviderRepository _providers;
    private readonly IServiceRequestMessageRepository _messages;

    public GetServiceRequestMessagesQueryHandler(
        IServiceRequestRepository requests,
        IServiceProviderRepository providers,
        IServiceRequestMessageRepository messages)
    {
        _requests = requests;
        _providers = providers;
        _messages = messages;
    }

    public async Task<IReadOnlyList<ServiceRequestMessageDto>> Handle(
        GetServiceRequestMessagesQuery query, CancellationToken ct)
    {
        var (request, provider, party) = await ServiceRequestParties.ResolveAsync(
            _requests, _providers, query.ServiceRequestId, query.ActorUserId, query.IsAdmin, ct);

        var thread = await _messages.GetThreadAsync(request.Id, ct);
        return ToDtos(request, provider, party, thread);
    }

    internal static IReadOnlyList<ServiceRequestMessageDto> ToDtos(
        ServiceRequest request, ServiceProvider provider, ServiceParty? viewer, IReadOnlyList<ServiceRequestMessage> thread)
    {
        var requesterName = ServiceRequestParties.NameOf(request.Requester);

        return thread
            .Select(m => new ServiceRequestMessageDto(
                m.Id,
                m.ServiceRequestId,
                m.SenderParty,
                m.SenderParty == ServiceParty.Requester ? requesterName : provider.DisplayName,
                m.Body,
                m.CreatedAt,
                m.ReadAt,
                IsMine: viewer is not null && m.SenderParty == viewer))
            .ToList()
            .AsReadOnly();
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Mark the other side's messages as read
// ─────────────────────────────────────────────────────────────────────────────

public sealed record MarkServiceRequestMessagesReadCommand(Guid ServiceRequestId, Guid ActorUserId) : IRequest<int>;

public sealed class MarkServiceRequestMessagesReadCommandHandler
    : IRequestHandler<MarkServiceRequestMessagesReadCommand, int>
{
    private readonly IServiceRequestRepository _requests;
    private readonly IServiceProviderRepository _providers;
    private readonly IServiceRequestMessageRepository _messages;
    private readonly IUnitOfWork _uow;

    public MarkServiceRequestMessagesReadCommandHandler(
        IServiceRequestRepository requests,
        IServiceProviderRepository providers,
        IServiceRequestMessageRepository messages,
        IUnitOfWork uow)
    {
        _requests = requests;
        _providers = providers;
        _messages = messages;
        _uow = uow;
    }

    public async Task<int> Handle(MarkServiceRequestMessagesReadCommand command, CancellationToken ct)
    {
        var (request, _, party) = await ServiceRequestParties.ResolveAsync(
            _requests, _providers, command.ServiceRequestId, command.ActorUserId, isAdmin: false, ct);

        var unread = await _messages.GetUnreadForAsync(request.Id, party!.Value, ct);
        if (unread.Count == 0)
            return 0;

        var now = DateTime.UtcNow;
        foreach (var message in unread)
            message.MarkRead(now);

        await _uow.SaveChangesAsync(ct);
        return unread.Count;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// The customer confirms: an inquiry becomes a request
// ─────────────────────────────────────────────────────────────────────────────

public sealed record ConfirmServiceRequestCommand(Guid ServiceRequestId, Guid ActorUserId)
    : IRequest<ServiceRequestDto>;

public sealed class ConfirmServiceRequestCommandHandler
    : IRequestHandler<ConfirmServiceRequestCommand, ServiceRequestDto>
{
    private readonly IServiceRequestRepository _requests;
    private readonly IServiceProviderRepository _providers;
    private readonly IServiceRequestStatusHistoryRepository _history;
    private readonly IServiceRequestMessageRepository _messages;
    private readonly IUnitOfWork _uow;
    private readonly INotificationService _notifications;
    private readonly ILogger<ConfirmServiceRequestCommandHandler> _logger;

    public ConfirmServiceRequestCommandHandler(
        IServiceRequestRepository requests,
        IServiceProviderRepository providers,
        IServiceRequestStatusHistoryRepository history,
        IServiceRequestMessageRepository messages,
        IUnitOfWork uow,
        INotificationService notifications,
        ILogger<ConfirmServiceRequestCommandHandler> logger)
    {
        _requests = requests;
        _providers = providers;
        _history = history;
        _messages = messages;
        _uow = uow;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<ServiceRequestDto> Handle(ConfirmServiceRequestCommand command, CancellationToken ct)
    {
        var (request, provider, party) = await ServiceRequestParties.ResolveAsync(
            _requests, _providers, command.ServiceRequestId, command.ActorUserId, isAdmin: false, ct);

        if (party != ServiceParty.Requester)
            throw new ForbiddenException("وحده صاحب الطلب يستطيع تأكيده.");

        if (!provider.IsActive)
            throw new ConflictException("مزوّد هذه الخدمة غير نشط حالياً.");

        // "After the customer's first contact": the confirmation follows at least one message.
        var thread = await _messages.GetThreadAsync(request.Id, ct);
        if (!thread.Any(m => m.SenderParty == ServiceParty.Requester))
            throw new ConflictException("اكتب رسالتك للمزوّد أولًا ثم أكّد الطلب.");

        var now = DateTime.UtcNow;
        request.Confirm(now);

        await _history.AddAsync(
            ServiceRequestStatusHistory.Record(
                request.Id, ServiceRequestStatus.Inquiry, request.Status, command.ActorUserId,
                "أكّد العميل الطلب بعد التواصل مع المزوّد.", now),
            ct);

        await _uow.SaveChangesAsync(ct);

        try
        {
            await _notifications.NotifyPropertyUpdateAsync(
                recipientId: provider.UserId,
                propertyId: request.PropertyId,
                propertyTitle: request.Property?.Title ?? "-",
                type: NotificationType.ServiceRequestSubmitted,
                detail: NotificationText.Of(
                    NotificationKeys.ServiceSubmittedDetail,
                    ("number", request.RequestNumber), ("title", request.Property?.Title ?? "-")),
                relatedEntityId: request.Id,
                ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send service-request-submitted notification. ServiceRequestId={ServiceRequestId}",
                request.Id);
        }

        var reloaded = await _requests.GetByIdAsync(request.Id, ct) ?? request;
        var stats = await _messages.GetStatsAsync([reloaded.Id], ServiceParty.Requester, ct);
        return ServiceMapper.ToDto(
            reloaded, ServiceRequestAudience.Requester,
            stats.GetValueOrDefault(reloaded.Id, ServiceRequestMessageStats.Empty));
    }
}
