using PropertyApi.Application.Common.Security;

namespace PropertyApi.Application.Common.Interfaces;

public interface IUserSecurityStampReader
{
    Task<SecurityStampSnapshot?> GetSecurityStampAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}

