using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HudhudNestApi.Application.Common.Exceptions;

public sealed class ConflictException : Exception
{
    public ConflictException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// A conflict with a stable, translatable code for a localised client (same idea as
    /// <see cref="ForbiddenException.Code"/>). Null for call sites that only carry a message.
    /// </summary>
    public ConflictException(string message, string? code)
        : base(message)
    {
        Code = code;
    }

    public string? Code { get; }
}

