using HudhudNestApi.Application.Notifications.Push;

namespace HudhudNestApi.Infrastructure.Notifications.Push;

/// <summary>Used where push is not configured (development, tests, an environment without FCM credentials).</summary>
public sealed class NullPushSender : IPushSender
{
    public Task<PushSendStatus> SendAsync(string deviceToken, PushPayload payload, CancellationToken ct = default) =>
        Task.FromResult(PushSendStatus.Disabled);
}
