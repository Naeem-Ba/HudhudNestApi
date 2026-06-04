using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyApi.Infrastructure.Identity.Entities;

namespace PropertyApi.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF configuration for RefreshToken (now in Infrastructure.Identity.Entities).
///
/// PATH FIX: File must be in Infrastructure/Persistence/Configurations/
///           so ApplyConfigurationsFromAssembly() picks it up.
///           The old location (Infrastructure/Configurations/) was outside
///           the Persistence namespace, causing it to be silently ignored.
/// </summary>
public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Token)
            .IsRequired()
            .HasMaxLength(512);

        builder.Property(t => t.ReplacedByToken)
            .HasMaxLength(512);

        builder.Property(t => t.CreatedByIp)
            .HasMaxLength(64);

        builder.Property(t => t.RevokedByIp)
            .HasMaxLength(64);

        // FK
        builder.HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Index for token lookup (login + refresh flows hit this constantly)
        builder.HasIndex(t => t.Token).IsUnique();
        builder.HasIndex(t => t.UserId);
    }
}

