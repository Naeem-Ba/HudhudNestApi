using Microsoft.AspNetCore.Identity;
using HudhudNestApi.Domain.Users.Constants;
using ApplicationRole = HudhudNestApi.Infrastructure.Identity.Entities.ApplicationRole;

namespace HudhudNestApi.Infrastructure.Persistence.Seeds;

/// <summary>
/// يضيف أدوار النظام بالعربية والإنجليزية.
/// يستخدم RoleManager<ApplicationRole> حتى لا يتعارض مع Identity.
/// </summary>
public static class ApplicationRolesSeed
{
    private static readonly IReadOnlyDictionary<string, string> ArabicNames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RoleNames.Admin] = "مدير المنصة",
            [RoleNames.Agent] = "وسيط عقاري",
            [RoleNames.User] = "مستخدم",
            [RoleNames.AgencyOwner] = "مالك مكتب عقاري",
            [RoleNames.AgencyAgent] = "موظف مكتب عقاري",
        };

    public static async Task SeedAsync(RoleManager<ApplicationRole> roleManager)
    {
        foreach (var roleName in ArabicNames.Keys)
        {
            await EnsureAsync(roleManager, roleName);
        }
    }

    /// <summary>
    /// True when <paramref name="roleName"/> is one of the platform's own roles, the only ones
    /// this class knows how to create.
    /// </summary>
    public static bool IsSystemRole(string roleName)
        => ArabicNames.ContainsKey(roleName);

    /// <summary>
    /// Creates one system role when it is missing. Returns true when it had to create it.
    /// </summary>
    public static async Task<bool> EnsureAsync(
        RoleManager<ApplicationRole> roleManager,
        string roleName)
    {
        if (!ArabicNames.TryGetValue(roleName, out var arabicName))
        {
            throw new ArgumentException(
                $"'{roleName}' is not a system role.", nameof(roleName));
        }

        if (await roleManager.RoleExistsAsync(roleName))
            return false;

        var result = await roleManager.CreateAsync(
            new ApplicationRole(roleName, arabicName));

        if (!result.Succeeded)
        {
            var errors = string.Join(
                ", ",
                result.Errors.Select(e => e.Description));

            throw new InvalidOperationException(
                $"Failed to seed role '{roleName}'. Errors: {errors}");
        }

        return true;
    }
}
