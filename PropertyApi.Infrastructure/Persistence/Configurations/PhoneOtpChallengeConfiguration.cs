using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyApi.Domain.Auth.Entities;

namespace PropertyApi.Infrastructure.Persistence.Configurations;

public sealed class PhoneOtpChallengeConfiguration : IEntityTypeConfiguration<PhoneOtpChallenge>
{
    public void Configure(EntityTypeBuilder<PhoneOtpChallenge> builder)
    {
        builder.ToTable("PhoneOtpChallenges");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.NormalizedPhoneNumber).HasMaxLength(16).IsRequired();
        builder.Property(x => x.CodeHash).HasMaxLength(128).IsRequired();
        builder.HasIndex(x => new { x.NormalizedPhoneNumber, x.Purpose, x.CreatedAtUtc });
        builder.HasIndex(x => x.ExpiresAtUtc);
    }
}
