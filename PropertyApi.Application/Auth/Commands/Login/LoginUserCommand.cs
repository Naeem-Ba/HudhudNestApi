using MediatR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PropertyApi.Application.Auth.Commands.Login
{
    public sealed record LoginUserCommand(string Email, string Password)
       : IRequest<LoginResult>;

    public sealed record LoginResult(
        string AccessToken,
        string RefreshToken,
        int ExpiresIn);
}
