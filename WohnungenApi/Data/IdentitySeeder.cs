using Microsoft.AspNetCore.Identity;
using WohnungenApi.Models;

namespace WohnungenApi.Data
{
    public static class IdentitySeeder
    {
        public static async Task SeedAsync(
            UserManager<Benutzer> userManager,
            RoleManager<IdentityRole<int>> roleManager)
        {
            // إنشاء الأدوار
            if (!await roleManager.RoleExistsAsync("Admin"))
                await roleManager.CreateAsync(new IdentityRole<int>("Admin"));

            if (!await roleManager.RoleExistsAsync("User"))
                await roleManager.CreateAsync(new IdentityRole<int>("User"));

            // إنشاء Admin افتراضي
            var adminEmail = "admin@admin.com";

            if (await userManager.FindByEmailAsync(adminEmail) == null)
            {
                var admin = new Benutzer
                {
                    UserName = adminEmail,
                    Email = adminEmail,
                    NormalizedEmail = adminEmail.ToUpper(),
                    EmailConfirmed = true,
                    DisplayName = "System Admin"
                };

                await userManager.CreateAsync(admin, "Admin123!");
                await userManager.AddToRoleAsync(admin, "Admin");
            }
        }
    }
}