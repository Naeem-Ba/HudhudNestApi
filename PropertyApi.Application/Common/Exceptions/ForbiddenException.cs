using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PropertyApi.Application.Common.Exceptions;

public sealed class ForbiddenException : Exception
{
    /// <summary>
    /// Stable, translatable code for a localised client — the same idea as
    /// ValidationException.ErrorCodes, but for the single-message 403 case.
    /// Null for call sites that predate this and only ever carried a message.
    /// </summary>
    public string? Code { get; }

    public ForbiddenException(string message, string? code = null)
        : base(message)
    {
        Code = code;
    }
}
