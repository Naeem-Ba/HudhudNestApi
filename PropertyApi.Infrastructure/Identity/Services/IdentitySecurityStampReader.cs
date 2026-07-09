using Microsoft.AspNetCore.Identity;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Common.Security;
using PropertyApi.Infrastructure.Identity.Entities;

namespace PropertyApi.Infrastructure.Identity.Services;

public sealed class IdentitySecurityStampReader : IUserSecurityStampReader
{
    private readonly UserManager<ApplicationUser> _userManager;

    public IdentitySecurityStampReader(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<SecurityStampSnapshot?> GetSecurityStampAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await _userManager.FindByIdAsync(userId.ToString());

        if (user is null)
            return null;

        return new SecurityStampSnapshot(
            user.SecurityStamp ?? string.Empty,
            user.IsDeleted);
    }
}
