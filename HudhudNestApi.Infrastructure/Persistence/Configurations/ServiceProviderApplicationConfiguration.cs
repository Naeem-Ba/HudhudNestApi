using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.Services.Entities;
using HudhudNestApi.Domain.Users.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations;

public sealed class ServiceProviderApplicationConfiguration : IEntityTypeConfiguration<ServiceProviderApplication>
{
    public const string NumberSequence = "ServiceProviderApplicationNumberSeq";

    public void Configure(EntityTypeBuilder<ServiceProviderApplication> builder)
    {
        builder.ToTable("ServiceProviderApplications");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Number)
            .HasDefaultValueSql($"nextval('\"{NumberSequence}\"')")
            .ValueGeneratedOnAdd();
        builder.HasIndex(a => a.Number).IsUnique();
        builder.Ignore(a => a.ReferenceNumber);
        builder.Ignore(a => a.IsOpen);

        builder.Property(a => a.ApplicantType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(a => a.DisplayName).IsRequired().HasMaxLength(ServiceProviderApplication.MaxDisplayNameLength);
        builder.Property(a => a.ContactName).IsRequired().HasMaxLength(ServiceProviderApplication.MaxContactNameLength);
        builder.Property(a => a.ContactEmail).HasMaxLength(ServiceProviderApplication.MaxEmailLength);
        builder.Property(a => a.ContactPhone).HasMaxLength(ServiceProviderApplication.MaxPhoneLength);
        builder.Property(a => a.Experience).IsRequired().HasMaxLength(ServiceProviderApplication.MaxExperienceLength);
        builder.Property(a => a.CompanyName).HasMaxLength(ServiceProviderApplication.MaxCompanyNameLength);
        builder.Property(a => a.LicenseNumber).HasMaxLength(ServiceProviderApplication.MaxLicenseNumberLength);
        builder.Property(a => a.InfoRequest).HasMaxLength(ServiceProviderApplication.MaxMessageLength);
        builder.Property(a => a.DecisionReason).HasMaxLength(ServiceProviderApplication.MaxMessageLength);

        // Same native-array approach as Plan.FeatureKeys: short, bounded lists read whole.
        builder.Property(a => a.RequestedCategories).HasColumnType("text[]").IsRequired();
        builder.Property(a => a.RequestedGovernorateIds).HasColumnType("integer[]").IsRequired();

        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(a => a.ApplicantUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ServiceProvider>()
            .WithMany()
            .HasForeignKey(a => a.ServiceProviderId)
            .OnDelete(DeleteBehavior.Restrict);

        // At most one open application per account: a double-click or a retry after a lost
        // response can never leave two applications under review for the same person.
        builder.HasIndex(a => a.ApplicantUserId)
            .IsUnique()
            .HasDatabaseName("IX_ServiceProviderApplications_OpenPerApplicant")
            .HasFilter("\"IsDeleted\" = false AND \"Status\" IN ('Pending', 'NeedsInfo')");

        builder.HasIndex(a => new { a.Status, a.CreatedAt });

        // Optimistic concurrency, same xmin pattern as AppRelease/Property: two admins deciding
        // the same application at once get a 409 instead of the second silently overwriting.
        builder.Property<uint>("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsRowVersion();

        builder.HasQueryFilter(a => !a.IsDeleted);
    }
}
