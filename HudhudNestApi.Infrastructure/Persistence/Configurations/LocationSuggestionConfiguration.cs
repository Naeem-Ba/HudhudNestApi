using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.Lookups.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations;

public sealed class LocationSuggestionConfiguration : IEntityTypeConfiguration<LocationSuggestion>
{
    public void Configure(EntityTypeBuilder<LocationSuggestion> builder)
    {
        builder.ToTable("LocationSuggestions");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.Name).IsRequired().HasMaxLength(150);
        builder.Property(s => s.ReviewNotes).HasMaxLength(500);

        // No FK to Governorates/Districts here on purpose: ParentId's target
        // table depends on Type (Governorates for District suggestions,
        // Districts for Neighborhood suggestions) — a polymorphic reference
        // EF can't express as a single FK. Validated in the application layer
        // instead (see LocationSuggestionService).
        builder.HasIndex(s => new { s.Type, s.ParentId, s.Status });
        builder.HasIndex(s => s.Status);

        // Best-effort link back to the listing that triggered the suggestion,
        // for admin context only — never required, and must survive the
        // property being deleted later.
        builder.HasOne<HudhudNestApi.Domain.Listings.Entities.Property>()
            .WithMany()
            .HasForeignKey(s => s.PropertyId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<HudhudNestApi.Domain.Users.Entities.UserAccount>()
            .WithMany()
            .HasForeignKey(s => s.SubmittedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
