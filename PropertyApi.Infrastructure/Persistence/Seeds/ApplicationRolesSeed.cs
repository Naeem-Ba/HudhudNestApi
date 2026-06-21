using Microsoft.AspNetCore.Identity;
using PropertyApi.Domain.Users.Constants;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Infrastructure.Persistence.Seeds;

/// <summary>
/// يضيف أدوار النظام بالعربية والإنجليزية.
/// يستخدم RoleManager<ApplicationRole> حتى لا يتعارض مع Identity.
/// </summary>
public static class ApplicationRolesSeed
{
    public static async Task SeedAsync(RoleManager<ApplicationRole> roleManager)
    {
        var roles = new[]
        {
            new ApplicationRole(RoleNames.Admin, "مدير المنصة"),
            new ApplicationRole(RoleNames.Agent, "وسيط عقاري"),
            new ApplicationRole(RoleNames.User, "مستخدم"),
        };

        foreach (var role in roles)
        {
            if (string.IsNullOrWhiteSpace(role.Name))
                continue;

            if (await roleManager.RoleExistsAsync(role.Name))
                continue;

            var result = await roleManager.CreateAsync(role);

            if (!result.Succeeded)
            {
                var errors = string.Join(
                    ", ",
                    result.Errors.Select(e => e.Description));

                throw new InvalidOperationException(
                    $"Failed to seed role '{role.Name}'. Errors: {errors}");
            }
        }
    }
}