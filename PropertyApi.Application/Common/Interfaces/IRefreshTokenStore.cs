using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PropertyApi.Application.Common.Interfaces;

public interface IRefreshTokenStore
{
    Task StoreAsync(
        Guid userId,
        string refreshToken,
        string? createdByIp,
        CancellationToken ct = default);
}
