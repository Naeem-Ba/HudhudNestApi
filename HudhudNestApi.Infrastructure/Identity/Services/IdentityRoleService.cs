using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Auth.Interfaces;
using HudhudNestApi.Application.Auth.Models;
using ApplicationRole = HudhudNestApi.Infrastructure.Identity.Entities.ApplicationRole;

namespace HudhudNestApi.Infrastructure.Identity.Services;

/// <summary>
/// Infrastructure implementation of role-management operations backed by ASP.NET Identity.
/// Keeps RoleManager and IdentityResult outside the Application layer.
/// </summary>
public sealed class IdentityRoleService : IIdentityRoleService
{
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly ILogger<IdentityRoleService> _logger;

    public IdentityRoleService(
        RoleManager<ApplicationRole> roleManager,
        ILogger<IdentityRoleService> logger)
    {
        _roleManager = roleManager;
        _logger = logger;
    }

    public Task<bool> RoleExistsAsync(string role, CancellationToken ct = default)
        => _roleManager.RoleExistsAsync(role);

    public async Task<IdentityOperationResult> CreateRoleAsync(
        string role,
        CancellationToken ct = default)
    {
        var result = await _roleManager.CreateAsync(
            new ApplicationRole(role, role));

        return Map(result);
    }

    private IdentityOperationResult Map(IdentityResult result)
    {
        if (result.Succeeded)
            return IdentityOperationResult.Success();

        var errors = result.Errors
            .Select(error => error.Description)
            .ToArray();

        _logger.LogDebug(
            "Identity role operation failed. Errors: {Errors}",
            string.Join(", ", errors));

        return IdentityOperationResult.Failed(errors);
    }
}
