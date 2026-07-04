using Microsoft.AspNetCore.Identity;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Infrastructure.Identity.Entities;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public Guid UserAccountId { get; set; }

    public UserAccount UserAccount { get; set; } = null!;
}