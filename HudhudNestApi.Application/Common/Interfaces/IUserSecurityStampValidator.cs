using HudhudNestApi.Application.Common.Security;

namespace HudhudNestApi.Application.Common.Interfaces;

public interface IUserSecurityStampValidator
{
    Task<SecurityStampValidationResult> ValidateAsync(
        Guid userId,
        string tokenSecurityStamp,
        CancellationToken cancellationToken = default);
}

