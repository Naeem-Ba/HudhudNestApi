using Microsoft.Extensions.Options;
using HudhudNestApi.Application.Users.Interfaces;

namespace HudhudNestApi.Infrastructure.Users;

public sealed class AccountDeletionSettings : IAccountDeletionSettings
{
    private readonly AccountDeletionOptions _options;

    public AccountDeletionSettings(IOptions<AccountDeletionOptions> options)
        => _options = options.Value;

    public int DelayDays => _options.DelayDays;
}
