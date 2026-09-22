using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.Notifications.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations;

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");

        builder.HasKey(notification => notification.Id);

        builder.Property(notification => notification.RecipientId)
            .IsRequired();

        builder.Property(notification => notification.Type)
            .IsRequired();

        builder.Property(notification => notification.Message)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(notification => notification.IsRead)
            .HasDefaultValue(false)
            .IsRequired();

        builder.HasIndex(notification => new
        {
            notification.RecipientId,
            notification.IsRead,
            notification.CreatedAt
        })
            .HasDatabaseName("IX_Notifications_Recipient_Read_CreatedAt");

        builder.HasIndex(notification => notification.PropertyId)
            .HasDatabaseName("IX_Notifications_PropertyId");

        builder.HasOne(notification => notification.Recipient)
            .WithMany()
            .HasForeignKey(notification => notification.RecipientId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
