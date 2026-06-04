using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyApi.Domain.Messaging.Entities;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Infrastructure.Persistence.Configurations;

public sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("Messages");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Content)
            .IsRequired()
            .HasMaxLength(3000);

        builder.Property(m => m.SenderId)
            .IsRequired();

        builder.Property(m => m.ReceiverId)
            .IsRequired();

        builder.Property(m => m.PropertyId)
            .IsRequired();

        builder.Property(m => m.IsRead)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(m => m.ReadAt)
            .IsRequired(false);

        builder.HasOne(m => m.Property)
            .WithMany(p => p.Messages)
            .HasForeignKey(m => m.PropertyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.Sender)
            .WithMany(u => u.Messages)
            .HasForeignKey(m => m.SenderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(m => m.ReceiverId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(m => m.PropertyId);
        builder.HasIndex(m => m.SenderId);
        builder.HasIndex(m => m.ReceiverId);
        builder.HasIndex(m => m.IsRead);
        builder.HasIndex(m => new { m.SenderId, m.ReceiverId, m.PropertyId });
    }
}
