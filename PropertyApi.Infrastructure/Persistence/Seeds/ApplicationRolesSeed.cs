using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using PropertyApi.Domain.Users.Entities;
using PropertyApi.Domain.Users.Constants;
using Microsoft.AspNetCore.Identity;

namespace PropertyApi.Infrastructure.Persistence.Seeds;

/// <summary>
/// يُضيف أدوار النظام بالعربية والإنجليزية.
/// يستخدم RoleManager<ApplicationRole> ← لا يتعارض مع Identity.
/// </summary>
public static class ApplicationRolesSeed
{
    public static async Task SeedAsync(RoleManager<ApplicationRole> roleManager)
    {
        var roles = new[]
        {
            new ApplicationRole(RoleNames.Admin,  "مدير المنصة"),
            new ApplicationRole(RoleNames.Agent,  "وسيط عقاري"),
            new ApplicationRole(RoleNames.User,   "مستخدم"),
        };

        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role.Name!))
                await roleManager.CreateAsync(role);
        }
    }
}