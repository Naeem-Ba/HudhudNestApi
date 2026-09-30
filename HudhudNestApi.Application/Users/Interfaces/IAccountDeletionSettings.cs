namespace HudhudNestApi.Application.Users.Interfaces;

/// <summary>
/// Finding F7 (docs/ACCOUNT-DELETION-PRODUCTION-READINESS.md): the delay window between a
/// self-service deletion request and the sweep actually executing it. Not hardcoded in
/// <c>RequestDeleteUserCommandHandler</c> -- mirrors <see cref="HudhudNestApi.Application.Auth.Interfaces.IJwtTokenSettings"/>'s
/// shape so a configuration value can cross the Application/Infrastructure boundary without
/// Application depending on <c>Microsoft.Extensions.Configuration</c> directly.
/// </summary>
public interface IAccountDeletionSettings
{
    /// <summary>
    /// Days between a deletion request and the scheduled sweep executing it. See
    /// <c>AccountDeletion:DelayDays</c> in appsettings.json for the configured value and its
    /// own documentation of why this number was chosen.
    /// </summary>
    int DelayDays { get; }
}
