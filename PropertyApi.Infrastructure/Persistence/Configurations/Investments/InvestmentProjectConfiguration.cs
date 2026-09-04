using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyApi.Domain.Investments.Entities;
using PropertyApi.Domain.Listings.Entities;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Infrastructure.Persistence.Configurations.Investments;

public sealed class InvestmentProjectConfiguration : IEntityTypeConfiguration<InvestmentProject>
{
    public void Configure(EntityTypeBuilder<InvestmentProject> builder)
    {
        builder.ToTable("InvestmentProjects");
        builder.HasKey(p => p.Id);

        // Optimistic concurrency — same xmin pattern as Property/ShortStayListing. An admin
        // editing a project while another admin transitions its status concurrently gets a
        // clean DbUpdateConcurrencyException (-> 409) instead of a silent last-write-wins.
        builder.Property<uint>("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsRowVersion();

        builder.Property(p => p.Title).IsRequired().HasMaxLength(200);
        builder.Property(p => p.ShortDescription).HasMaxLength(300);
        builder.Property(p => p.Description).IsRequired().HasMaxLength(8000);
        builder.Property(p => p.RejectionReason).HasMaxLength(1000);

        builder.Property(p => p.ProjectType).HasConversion<string>().HasMaxLength(30);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.RiskLevel).HasConversion<string>().HasMaxLength(20);

        builder.Property(p => p.Currency).IsRequired().HasMaxLength(3);

        builder.Property(p => p.TargetAmount).HasColumnType("decimal(18,4)");
        builder.Property(p => p.MinimumInvestment).HasColumnType("decimal(18,4)");
        builder.Property(p => p.MaximumInvestment).HasColumnType("decimal(18,4)");
        builder.Property(p => p.RaisedAmount).HasColumnType("decimal(18,4)");
        builder.Property(p => p.ExpectedReturnMin).HasColumnType("decimal(9,4)");
        builder.Property(p => p.ExpectedReturnMax).HasColumnType("decimal(9,4)");

        // FK-only relationships (no navigation collections) so list/detail queries never pull
        // Property's or the owner's full graph — Phase 1 spec §32 performance rule.
        builder.HasOne<Property>()
            .WithMany()
            .HasForeignKey(p => p.PropertyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(p => p.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => p.Status);
        builder.HasIndex(p => p.PropertyId);
        builder.HasIndex(p => p.StartDate);
        builder.HasIndex(p => p.EndDate);
        builder.HasIndex(p => p.ProjectType);

        builder.HasQueryFilter(p => !p.IsDeleted);
    }
}
