namespace HudhudNestApi.Application.Admin.Interfaces;

/// <summary>What an administrator just did to another account.</summary>
public enum AdminAccountAction
{
    RoleChanged = 1,
    Disabled = 2,
    Enabled = 3
}

/// <summary>
/// Tells the affected account owner and every other administrator that a sensitive admin action happened
/// (QA phase 17, P17-D07). An admin token that is stolen or misused can otherwise grant the Admin role or lock
/// an account without anyone noticing. It never throws and never blocks the admin operation.
/// </summary>
public interface IAdminActionNotifier
{
    /// <param name="detail">Short plain text, for example the role names after the change.</param>
    Task NotifyAsync(
        AdminAccountAction action,
        Guid targetUserId,
        Guid actorUserId,
        string? detail,
        CancellationToken ct = default);
}
