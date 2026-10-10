namespace HudhudNestApi.Infrastructure.Notifications.Push;

/// <summary>
/// <c>Push:Fcm</c> configuration. Nothing here has a usable default: with no project id and no
/// service account the sender reports <c>Disabled</c> and the in-app (SignalR) notification is
/// unaffected. The service-account JSON is a secret: supply it through the environment / secret
/// store (<c>Push__Fcm__ServiceAccountJson</c>), never a committed file.
/// </summary>
public sealed class PushOptions
{
    public const string SectionName = "Push:Fcm";

    public bool Enabled { get; set; }

    public string? ProjectId { get; set; }

    /// <summary>The Google service-account key as JSON text.</summary>
    public string? ServiceAccountJson { get; set; }

    public bool IsConfigured =>
        Enabled
        && !string.IsNullOrWhiteSpace(ProjectId)
        && !string.IsNullOrWhiteSpace(ServiceAccountJson);
}
