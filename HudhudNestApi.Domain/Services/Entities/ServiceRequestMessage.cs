using HudhudNestApi.Domain.Common.Entities;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Services.Enums;

namespace HudhudNestApi.Domain.Services.Entities;

/// <summary>
/// One message between the customer and the provider of a service request. Append-only: a sent
/// message is never edited (the thread is also the evidence in a dispute); only ReadAt is stamped
/// when the other side opens it.
/// </summary>
public sealed class ServiceRequestMessage : BaseEntity
{
    private ServiceRequestMessage() { }

    public Guid ServiceRequestId { get; private set; }

    public Guid SenderUserId { get; private set; }

    public ServiceParty SenderParty { get; private set; }

    public string Body { get; private set; } = string.Empty;

    /// <summary>When the other side opened the thread after this message was sent.</summary>
    public DateTime? ReadAt { get; private set; }

    public static ServiceRequestMessage Create(
        Guid serviceRequestId, Guid senderUserId, ServiceParty senderParty, string? body, DateTime utcNow)
    {
        if (serviceRequestId == Guid.Empty || senderUserId == Guid.Empty)
            throw new DomainException("الرسالة بلا طلب أو مرسل.");

        if (!Enum.IsDefined(senderParty))
            throw new DomainException("مرسل الرسالة غير معروف.");

        var text = body?.Trim();
        if (string.IsNullOrEmpty(text))
            throw new DomainException("اكتب رسالتك أولًا.");

        if (text.Length > ServiceRequestLifecyclePolicy.MaxMessageLength)
            throw new DomainException($"الرسالة أطول من {ServiceRequestLifecyclePolicy.MaxMessageLength} حرفًا.");

        return new ServiceRequestMessage
        {
            ServiceRequestId = serviceRequestId,
            SenderUserId = senderUserId,
            SenderParty = senderParty,
            Body = text,
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
        };
    }

    public void MarkRead(DateTime utcNow)
    {
        if (ReadAt is not null) return;
        ReadAt = utcNow;
        UpdatedAt = utcNow;
    }
}
