using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HudhudNestApi.Application.Common.Interfaces;

/// <summary>
/// Provides access to the currently authenticated user's identity.
/// Implemented in Infrastructure (reads from IHttpContextAccessor).
/// Application layer only sees this interface — no ASP.NET dependency.
///
/// BUG FIX: Was an empty "internal interface ICurrentUserService {}" — useless.
/// </summary>
public interface ICurrentUserService
{
    /// <summary>The authenticated user's Guid ID. Null if not authenticated.</summary>
    Guid? UserId { get; }

    /// <summary>The authenticated user's email claim.</summary>
    string? Email { get; }

    /// <summary>True if the request carries a valid authentication token.</summary>
    bool IsAuthenticated { get; }

    /// <summary>All role claims for the current user.</summary>
    IEnumerable<string> Roles { get; }
}

