using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.Messaging.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations;

public sealed class ContactMessageConfiguration : IEntityTypeConfiguration<ContactMessage>
{
    public void Configure(EntityTypeBuilder<ContactMessage> builder)
    {
        builder.ToTable("ContactMessages");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(c => c.Email)
            .IsRequired()
            .HasMaxLength(320);

        builder.Property(c => c.Subject)
            .HasMaxLength(300);

        builder.Property(c => c.Body)
            .IsRequired()
            .HasMaxLength(5000);

        builder.Property(c => c.IpAddress)
            .HasMaxLength(512);  // IPv6 max length

        builder.HasIndex(c => c.IsRead);
        builder.HasIndex(c => c.CreatedAt);
    }
}
