using Microsoft.Extensions.Options;
using PropertyApi.Application.Users.Interfaces;

namespace PropertyApi.Infrastructure.Users;

public sealed class AccountDeletionSettings : IAccountDeletionSettings
{
    private readonly AccountDeletionOptions _options;

    public AccountDeletionSettings(IOptions<AccountDeletionOptions> options)
        => _options = options.Value;

    public int DelayDays => _options.DelayDays;
}
