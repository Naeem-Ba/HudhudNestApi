using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PropertyApi.Domain.Users.Constants;

namespace PropertyApi.Seed;

public static class IdentitySeeder
{
    public static async Task SeedRolesAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();

        var roleManager = scope.ServiceProvider
            .GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        foreach (var desiredRoleName in RoleNames.All)
        {
            var normalizedName = desiredRoleName.ToUpperInvariant();

            var existingRole = await roleManager.Roles
                .SingleOrDefaultAsync(role =>
                    role.NormalizedName == normalizedName);

            if (existingRole is null)
            {
                var createResult = await roleManager.CreateAsync(
                    new IdentityRole<Guid>(desiredRoleName));

                EnsureSucceeded(
                    createResult,
                    $"Failed to create role '{desiredRoleName}'.");

                continue;
            }

            // This also repairs old values such as ADMIN -> Admin.
            if (!string.Equals(
                    existingRole.Name,
                    desiredRoleName,
                    StringComparison.Ordinal))
            {
                existingRole.Name = desiredRoleName;

                var updateResult = await roleManager.UpdateAsync(existingRole);

                EnsureSucceeded(
                    updateResult,
                    $"Failed to normalize role '{desiredRoleName}'.");
            }
        }
    }

    private static void EnsureSucceeded(
        IdentityResult result,
        string message)
    {
        if (result.Succeeded)
            return;

        var errors = string.Join(
            ", ",
            result.Errors.Select(error => error.Description));

        throw new InvalidOperationException($"{message} {errors}");
    }
}