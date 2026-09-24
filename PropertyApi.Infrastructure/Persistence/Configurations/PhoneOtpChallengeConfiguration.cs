using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyApi.Domain.Auth.Entities;
using PropertyApi.Domain.Enums;
using PropertyApi.Infrastructure.Identity.Entities;

namespace PropertyApi.Infrastructure.Persistence.Configurations;

public sealed class PhoneOtpChallengeConfiguration : IEntityTypeConfiguration<PhoneOtpChallenge>
{
    public void Configure(EntityTypeBuilder<PhoneOtpChallenge> builder)
    {
        builder.ToTable("PhoneOtpChallenges");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.NormalizedPhoneNumber).HasMaxLength(16).IsRequired();
        builder.Property(x => x.CodeHash).HasMaxLength(128).IsRequired();
        // Text, like the other enums in this codebase. Rows created before multi-channel OTP get "Sms" from the
        // column default, which is exactly how their code was delivered.
        builder.Property(x => x.Channel).HasConversion<string>().HasMaxLength(20).HasDefaultValue(OtpChannel.Sms).IsRequired();
        builder.Property(x => x.ProviderRequestId).HasMaxLength(128);
        builder.HasIndex(x => new { x.NormalizedPhoneNumber, x.Purpose, x.CreatedAtUtc });
        builder.HasIndex(x => x.ExpiresAtUtc);

        // Nullable: a challenge can exist before any identity does (first-time phone
        // registration). References Users (the Identity/auth table), not UserAccounts --
        // this is about which login the challenge is verifying, same target as
        // RefreshToken.UserId. SetNull: losing the identity should not take the challenge
        // history down with it.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
