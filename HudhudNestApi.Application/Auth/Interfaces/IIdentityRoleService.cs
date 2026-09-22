using HudhudNestApi.Application.Auth.Models;

namespace HudhudNestApi.Application.Auth.Interfaces;

/// <summary>
/// Application-level abstraction over ASP.NET Identity role management.
/// Infrastructure owns the concrete identity role service implementation.
/// </summary>
public interface IIdentityRoleService
{
    Task<bool> RoleExistsAsync(string role, CancellationToken ct = default);

    Task<IdentityOperationResult> CreateRoleAsync(string role, CancellationToken ct = default);
}

