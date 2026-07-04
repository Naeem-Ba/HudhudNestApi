using Microsoft.AspNetCore.Identity;

namespace PropertyApi.Infrastructure.Identity.Entities;

public sealed class ApplicationRole : IdentityRole<Guid>
{
    public string NameAr { get; set; } = string.Empty;

    public ApplicationRole() { }

    public ApplicationRole(string name, string nameAr)
    {
        Name = name;
        NormalizedName = name.ToUpperInvariant();
        NameAr = nameAr;
    }
}