using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.Notifications.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations;

public sealed class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> builder)
    {
        builder.ToTable("NotificationPreferences");
        builder.HasKey(p => p.UserId);

        builder.Property(p => p.MutedCategories).HasColumnType("text[]").IsRequired();
        builder.Property(p => p.PushMutedCategories).HasColumnType("text[]").IsRequired();
        builder.Property(p => p.PushPreview).HasDefaultValue(PushPreviewLevel.Generic).IsRequired();
        builder.Property(p => p.TimeZoneId).HasMaxLength(64);

        builder.HasOne<HudhudNestApi.Domain.Users.Entities.UserAccount>()
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
