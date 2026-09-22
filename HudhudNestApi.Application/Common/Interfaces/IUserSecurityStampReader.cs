using HudhudNestApi.Application.Common.Security;

namespace HudhudNestApi.Application.Common.Interfaces;

public interface IUserSecurityStampReader
{
    Task<SecurityStampSnapshot?> GetSecurityStampAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}

