using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Domain.Moderation;
using HudhudNestApi.Domain.Moderation.Entities;
using HudhudNestApi.Domain.Users.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations;

public sealed class ListingReportConfiguration : IEntityTypeConfiguration<ListingReport>
{
    public const string NumberSequence = "ListingReportNumberSeq";

    public void Configure(EntityTypeBuilder<ListingReport> builder)
    {
        builder.ToTable("ListingReports", table =>
        {
            // Defence in depth for the rules the domain already enforces.
            table.HasCheckConstraint("CK_ListingReports_Priority", "\"Priority\" BETWEEN 1 AND 4");
            table.HasCheckConstraint("CK_ListingReports_NotOwnListing", "\"ReporterUserId\" <> \"ListingOwnerId\"");
            table.HasCheckConstraint(
                "CK_ListingReports_ClosedHasOutcome",
                "(\"Status\" IN ('Resolved', 'Dismissed')) = (\"Outcome\" IS NOT NULL AND \"ResolvedAt\" IS NOT NULL)");
        });
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Number)
            .HasDefaultValueSql($"nextval('\"{NumberSequence}\"')")
            .ValueGeneratedOnAdd();
        builder.HasIndex(r => r.Number).IsUnique();
        builder.Ignore(r => r.ReferenceNumber);
        builder.Ignore(r => r.IsOpen);

        builder.Property(r => r.Reason).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        // A number, so "highest priority first" sorts in the database.
        builder.Property(r => r.Priority).HasConversion<short>().IsRequired();
        builder.Property(r => r.Outcome).HasConversion<string>().HasMaxLength(40);

        builder.Property(r => r.ListingTitleSnapshot).IsRequired().HasMaxLength(ListingReportPolicy.MaxTitleSnapshotLength);
        builder.Property(r => r.Details).HasMaxLength(ListingReportPolicy.MaxDetailsLength);
        builder.Property(r => r.InfoRequest).HasMaxLength(ListingReportPolicy.MaxNoteLength);
        builder.Property(r => r.ResolutionNote).HasMaxLength(ListingReportPolicy.MaxNoteLength);

        // Restrict everywhere: listings are soft-deleted and accounts are anonymised, never removed,
        // so a report keeps its history; and nothing may delete a report by cascading from elsewhere.
        builder.HasOne<Property>()
            .WithMany()
            .HasForeignKey(r => r.PropertyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(r => r.ReporterUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(r => r.AssignedAdminId)
            .OnDelete(DeleteBehavior.Restrict);

        // At most one open report per account and listing: a double tap or a race can never open two.
        builder.HasIndex(r => new { r.ReporterUserId, r.PropertyId })
            .IsUnique()
            .HasDatabaseName("IX_ListingReports_OpenPerReporterAndListing")
            .HasFilter("\"IsDeleted\" = false AND \"Status\" IN ('New', 'UnderReview', 'WaitingForInformation', 'ActionRequired')");

        // Replays of the same dialog submit (see ListingReport.ClientRequestId).
        builder.HasIndex(r => new { r.ReporterUserId, r.ClientRequestId })
            .IsUnique()
            .HasDatabaseName("IX_ListingReports_ClientRequest")
            .HasFilter("\"ClientRequestId\" IS NOT NULL");

        // Admin queue: by status and age, by priority, by listing, by assignee; daily limit by reporter.
        builder.HasIndex(r => new { r.Status, r.CreatedAt });
        builder.HasIndex(r => new { r.Priority, r.CreatedAt });
        builder.HasIndex(r => new { r.PropertyId, r.CreatedAt });
        builder.HasIndex(r => new { r.AssignedAdminId, r.Status });
        builder.HasIndex(r => new { r.ReporterUserId, r.CreatedAt });
        builder.HasIndex(r => r.ListingOwnerId);

        // Optimistic concurrency, same xmin pattern as ServiceProviderApplication.
        builder.Property<uint>("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsRowVersion();

        builder.HasQueryFilter(r => !r.IsDeleted);
    }
}

public sealed class ListingReportEventConfiguration : IEntityTypeConfiguration<ListingReportEvent>
{
    public void Configure(EntityTypeBuilder<ListingReportEvent> builder)
    {
        builder.ToTable("ListingReportEvents");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(e => e.FromValue).HasMaxLength(ListingReportEvent.MaxValueLength);
        builder.Property(e => e.ToValue).HasMaxLength(ListingReportEvent.MaxValueLength);
        builder.Property(e => e.Note).HasMaxLength(ListingReportPolicy.MaxNoteLength);

        builder.HasOne<ListingReport>()
            .WithMany()
            .HasForeignKey(e => e.ReportId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.ReportId, e.CreatedAt });

        builder.HasQueryFilter(e => !e.IsDeleted);
    }
}
