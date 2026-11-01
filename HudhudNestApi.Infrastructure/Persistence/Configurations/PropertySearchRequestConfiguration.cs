using HudhudNestApi.Domain.Demand.Entities;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Domain.Lookups.Entities;
using HudhudNestApi.Domain.Users.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations;

public sealed class PropertySearchRequestConfiguration : IEntityTypeConfiguration<PropertySearchRequest>
{
    public void Configure(EntityTypeBuilder<PropertySearchRequest> builder)
    {
        builder.ToTable("PropertySearchRequests", t =>
        {
            t.HasCheckConstraint("CK_PropertySearchRequests_Budget", "\"MaxBudget\" > 0 AND (\"MinBudget\" IS NULL OR \"MinBudget\" <= \"MaxBudget\")");
            t.HasCheckConstraint("CK_PropertySearchRequests_Area", "\"MinArea\" IS NULL OR \"MaxArea\" IS NULL OR \"MinArea\" <= \"MaxArea\"");
        });
        builder.HasKey(r => r.Id);

        builder.Property(r => r.ListingType).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.CurrencyCode).IsRequired().HasMaxLength(3).IsFixedLength();
        builder.Property(r => r.MinBudget).HasColumnType("decimal(18,4)");
        builder.Property(r => r.MaxBudget).HasColumnType("decimal(18,4)");
        builder.Property(r => r.MinArea).HasColumnType("decimal(10,2)");
        builder.Property(r => r.MaxArea).HasColumnType("decimal(10,2)");
        builder.Property(r => r.Details).HasMaxLength(PropertySearchRequest.DetailsMaxLength);

        // A deleted account takes its requests with it (the requester is the only person they concern).
        builder.HasOne<UserAccount>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Governorate>().WithMany().HasForeignKey(r => r.GovernorateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<District>().WithMany().HasForeignKey(r => r.DistrictId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Neighborhood>().WithMany().HasForeignKey(r => r.NeighborhoodId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PropertyType>().WithMany().HasForeignKey(r => r.PropertyTypeId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => new { r.UserId, r.Status });
        // Board and matcher: active requests of one governorate and type.
        builder.HasIndex(r => new { r.GovernorateId, r.ListingType, r.ExpiresAt })
            .HasDatabaseName("IX_PropertySearchRequests_Active")
            .HasFilter("\"Status\" = 'Active'");
    }
}

public sealed class SearchRequestMatchConfiguration : IEntityTypeConfiguration<SearchRequestMatch>
{
    public void Configure(EntityTypeBuilder<SearchRequestMatch> builder)
    {
        builder.ToTable("SearchRequestMatches", t =>
            t.HasCheckConstraint("CK_SearchRequestMatches_Score", "\"Score\" BETWEEN 0 AND 100"));
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Reasons).IsRequired().HasMaxLength(500);
        builder.Property(m => m.Source).HasConversion<string>().HasMaxLength(20);

        builder.HasOne<PropertySearchRequest>().WithMany().HasForeignKey(m => m.SearchRequestId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(m => m.Property).WithMany().HasForeignKey(m => m.PropertyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<UserAccount>().WithMany().HasForeignKey(m => m.ProposedByUserId).OnDelete(DeleteBehavior.SetNull);

        // Same filter as the listing: a match to a soft-deleted listing disappears from every query.
        builder.HasQueryFilter(m => !m.IsDeleted && !m.Property!.IsDeleted);

        builder.HasIndex(m => new { m.SearchRequestId, m.PropertyId }).IsUnique();
        builder.HasIndex(m => m.PropertyId);
        builder.HasIndex(m => new { m.ProposedByUserId, m.ProposedAt }).HasFilter("\"ProposedByUserId\" IS NOT NULL");
    }
}
