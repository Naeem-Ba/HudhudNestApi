using PropertyApi.Application.Common.Security;

namespace PropertyApi.Application.Common.Interfaces;

public interface IUserSecurityStampValidator
{
    Task<SecurityStampValidationResult> ValidateAsync(
        Guid userId,
        string tokenSecurityStamp,
        CancellationToken cancellationToken = default);
}

