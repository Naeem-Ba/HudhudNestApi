using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.Notifications.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations;

public sealed class DeviceTokenConfiguration : IEntityTypeConfiguration<DeviceToken>
{
    public void Configure(EntityTypeBuilder<DeviceToken> builder)
    {
        builder.ToTable("DeviceTokens");

        builder.HasKey(x => x.Id);

        // FCM registration tokens are ~163 chars today; 512 leaves room without being unbounded.
        builder.Property(x => x.Token)
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(x => x.Platform)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        // One row per install, whoever it currently belongs to.
        builder.HasIndex(x => x.Token)
            .IsUnique();

        builder.HasIndex(x => x.UserId);

        // The account is the owner: deleting it (GDPR erasure) takes its devices with it.
        builder.HasOne<HudhudNestApi.Domain.Users.Entities.UserAccount>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
