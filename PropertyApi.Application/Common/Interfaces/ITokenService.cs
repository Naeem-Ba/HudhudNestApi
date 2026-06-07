using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using PropertyApi.Domain.Users.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace PropertyApi.Application.Common.Interfaces
{
    public interface ITokenService
    {
        string GenerateAccessToken(User user, IReadOnlyCollection<string> roles);
        string GenerateRefreshToken();
    }

}
