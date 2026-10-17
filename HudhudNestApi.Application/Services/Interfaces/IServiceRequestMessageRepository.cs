using HudhudNestApi.Domain.Services.Entities;
using HudhudNestApi.Domain.Services.Enums;

namespace HudhudNestApi.Application.Services.Interfaces;

/// <summary>How a request's thread looks to one side: total, unread for that side, newest.</summary>
public sealed record ServiceRequestMessageStats(int Total, int Unread, DateTime? LastAt)
{
    public static readonly ServiceRequestMessageStats Empty = new(0, 0, null);
}

public interface IServiceRequestMessageRepository
{
    Task AddAsync(ServiceRequestMessage message, CancellationToken ct = default);

    /// <summary>The whole thread, oldest first (a thread is capped, see ServiceRequestLifecyclePolicy).</summary>
    Task<IReadOnlyList<ServiceRequestMessage>> GetThreadAsync(Guid serviceRequestId, CancellationToken ct = default);

    Task<int> CountAsync(Guid serviceRequestId, CancellationToken ct = default);

    /// <summary>The newest message of the thread, or null when there is none.</summary>
    Task<ServiceRequestMessage?> GetLastAsync(Guid serviceRequestId, CancellationToken ct = default);

    /// <summary>Tracked messages the <paramref name="reader"/> has not opened yet (sent by the other side).</summary>
    Task<IReadOnlyList<ServiceRequestMessage>> GetUnreadForAsync(
        Guid serviceRequestId, ServiceParty reader, CancellationToken ct = default);

    /// <summary>Stats for many requests at once, from <paramref name="viewer"/>'s point of view.</summary>
    Task<IReadOnlyDictionary<Guid, ServiceRequestMessageStats>> GetStatsAsync(
        IReadOnlyCollection<Guid> serviceRequestIds, ServiceParty viewer, CancellationToken ct = default);
}
