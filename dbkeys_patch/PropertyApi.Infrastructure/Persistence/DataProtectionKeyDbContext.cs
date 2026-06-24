using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace PropertyApi.Infrastructure.Persistence;

/// <summary>
/// Dedicated DbContext for ASP.NET Core Data Protection keys.
///
/// This context intentionally stays separate from AppDbContext to avoid a circular dependency:
/// AppDbContext uses IDataProtectionProvider for encrypted user fields, while Data Protection
/// itself needs a DbContext to persist its key ring.
/// </summary>
public sealed class DataProtectionKeyDbContext
    : DbContext, IDataProtectionKeyContext
{
    public DataProtectionKeyDbContext(DbContextOptions<DataProtectionKeyDbContext> options)
        : base(options)
    {
    }

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<DataProtectionKey>().ToTable("DataProtectionKeys");
    }
}
