using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.SocialDistribution.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations.SocialDistribution;

public sealed class SocialPublicationDeadLetterConfiguration : IEntityTypeConfiguration<SocialPublicationDeadLetter>
{
    public void Configure(EntityTypeBuilder<SocialPublicationDeadLetter> builder)
    {
        builder.ToTable("SocialPublicationDeadLetters");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Platform).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.LastErrorCode).IsRequired().HasConversion<string>().HasMaxLength(30);
        builder.Property(d => d.LastErrorMessage).IsRequired().HasMaxLength(500);
        builder.Property(d => d.ResolutionNote).HasMaxLength(500);

        // Restrict: a dead letter is a historical failure record and must outlive the publication
        // row it references remaining exactly as it was at failure time (no navigation property —
        // same FK-only pattern as the rest of this bounded context).
        builder.HasOne<SocialPublication>().WithMany().HasForeignKey(d => d.PublicationId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(d => d.PublicationId);
        builder.HasIndex(d => d.ResolvedAt);
        builder.HasIndex(d => d.FailedAt);
    }
}
