namespace PropertyApi.Infrastructure.Users;

/// <summary>
/// Finding F7 (docs/ACCOUNT-DELETION-PRODUCTION-READINESS.md). 30 days mirrors the delay window
/// most consumer platforms use for self-service account deletion (e.g. the interval commonly
/// cited by Google/Apple account-deletion guidance) -- an explicit default, not an invented
/// number, but still a business choice an operator can override via configuration without a
/// code change or redeploy.
/// </summary>
public sealed class AccountDeletionOptions
{
    public const string SectionName = "AccountDeletion";

    public int DelayDays { get; init; } = 30;
}
