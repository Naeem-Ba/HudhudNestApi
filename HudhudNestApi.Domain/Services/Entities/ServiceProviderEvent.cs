using HudhudNestApi.Domain.Common.Entities;
using HudhudNestApi.Domain.Services.Enums;

namespace HudhudNestApi.Domain.Services.Entities;

/// <summary>
/// Append-only timeline row for the provider-onboarding and provider-management workflow: one
/// table for both, so an admin opening a provider sees the application that created it and every
/// later decision in one ordered list. A row belongs to an application, a provider, or both (the
/// approval row carries both ids).
///
/// <see cref="IsInternal"/> rows are admin-only notes and decisions the applicant never sees;
/// the applicant's own view filters them out on the server, never in the client.
/// </summary>
public sealed class ServiceProviderEvent : BaseEntity
{
    private ServiceProviderEvent() { }

    public Guid? ApplicationId { get; private set; }

    public Guid? ServiceProviderId { get; private set; }

    /// <summary>Who did it. Null only for a system action.</summary>
    public Guid? ActorUserId { get; private set; }

    public ServiceProviderEventType Type { get; private set; }

    /// <summary>The message shown with the event (a reason, a note, what changed).</summary>
    public string? Note { get; private set; }

    public bool IsInternal { get; private set; }

    public static ServiceProviderEvent Record(
        Guid? applicationId,
        Guid? serviceProviderId,
        Guid? actorUserId,
        ServiceProviderEventType type,
        string? note,
        bool isInternal,
        DateTime utcNow)
    {
        if (applicationId is null && serviceProviderId is null)
            throw new ArgumentException("An event needs an application or a provider.");

        return new ServiceProviderEvent
        {
            ApplicationId = applicationId,
            ServiceProviderId = serviceProviderId,
            ActorUserId = actorUserId,
            Type = type,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            IsInternal = isInternal,
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
        };
    }
}
