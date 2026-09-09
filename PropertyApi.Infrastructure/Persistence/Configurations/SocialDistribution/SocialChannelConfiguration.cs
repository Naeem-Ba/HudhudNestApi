using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyApi.Domain.SocialDistribution.Entities;

namespace PropertyApi.Infrastructure.Persistence.Configurations.SocialDistribution;

public sealed class SocialChannelConfiguration : IEntityTypeConfiguration<SocialChannel>
{
    public void Configure(EntityTypeBuilder<SocialChannel> builder)
    {
        builder.ToTable("SocialChannels");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Platform).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Name).IsRequired().HasMaxLength(100);
        builder.Property(c => c.Status).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.ConfigurationVersion).HasMaxLength(50);

        // One channel per platform — see SocialChannel's class remarks for the rationale.
        builder.HasIndex(c => c.Platform).IsUnique();
    }
}
