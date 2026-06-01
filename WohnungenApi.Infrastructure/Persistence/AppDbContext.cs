using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using WohnungenApi.Domain.Listings.Entities;
using WohnungenApi.Domain.Messaging.Entities;
using WohnungenApi.Domain.Users.Entities;
using WohnungenApi.Infrastructure.Identity.Entities;

namespace WohnungenApi.Infrastructure.Persistence;

/// <summary>
/// Main database context.
/// Inherits IdentityDbContext with Guid PKs for User and Role.
/// Auto-stamps UpdatedAt on SaveChanges.
/// Global soft-delete filter applied to all BaseEntity types.
/// </summary>
public sealed class AppDbContext
    : IdentityDbContext<User, IdentityRole<Guid>, Guid>
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options) { }

    // ── DbSets ──────────────────────────────────────────────────
    public DbSet<Property> Properties => Set<Property>();
    public DbSet<PropertyImage> PropertyImages => Set<PropertyImage>();
    public DbSet<Amenity> Amenities => Set<Amenity>();
    public DbSet<PropertyAmenity> PropertyAmenities => Set<PropertyAmenity>();
    public DbSet<Favorite> Favorites => Set<Favorite>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<ContactMessage> ContactMessages => Set<ContactMessage>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    // ── Model Configuration ─────────────────────────────────────
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder); // ← REQUIRED: configures Identity tables

        // Load all IEntityTypeConfiguration<T> classes from this assembly
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // ── Rename Identity tables to clean English names ───────
        builder.Entity<User>().ToTable("Users");
        builder.Entity<IdentityRole<Guid>>().ToTable("Roles");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("UserRoles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("UserClaims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("UserLogins");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("RoleClaims");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("UserTokens");

        // ── Global soft-delete filter ───────────────────────────
        // Automatically excludes IsDeleted=true from ALL queries.
        // Override with .IgnoreQueryFilters() when you need deleted records.
        builder.Entity<Property>().HasQueryFilter(e => !e.IsDeleted);
        builder.Entity<PropertyImage>().HasQueryFilter(e => !e.IsDeleted);
        builder.Entity<Amenity>().HasQueryFilter(e => !e.IsDeleted);
        builder.Entity<Message>().HasQueryFilter(e => !e.IsDeleted);
        builder.Entity<ContactMessage>().HasQueryFilter(e => !e.IsDeleted);
        builder.Entity<User>().HasQueryFilter(e => !e.IsDeleted);
    }

    // ── Auto-stamp UpdatedAt on every save ──────────────────────
    public override async Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries())
        {
            // Stamp UpdatedAt on any modified entity that has the property
            if (entry.State == EntityState.Modified)
            {
                var updatedAt = entry.Properties
                    .FirstOrDefault(p => p.Metadata.Name == "UpdatedAt");
                if (updatedAt is not null)
                    updatedAt.CurrentValue = now;
            }

            // Intercept hard-delete and convert to soft-delete
            if (entry.State == EntityState.Deleted)
            {
                var isDeleted = entry.Properties
                    .FirstOrDefault(p => p.Metadata.Name == "IsDeleted");
                if (isDeleted is not null)
                {
                    entry.State = EntityState.Modified;
                    isDeleted.CurrentValue = true;

                    var deletedAt = entry.Properties
                        .FirstOrDefault(p => p.Metadata.Name == "DeletedAt");
                    if (deletedAt is not null)
                        deletedAt.CurrentValue = now;
                }
            }
        }

        return await base.SaveChangesAsync(cancellationToken);
    }
}