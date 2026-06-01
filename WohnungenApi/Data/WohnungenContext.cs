using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using WohnungenApi.Models;

namespace WohnungenApi.Data
{
    public class WohnungenContext
        : IdentityDbContext<Benutzer, IdentityRole<int>, int>
    {
        public WohnungenContext(DbContextOptions<WohnungenContext> options)
            : base(options)
        {
            AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        }

        public DbSet<ContactMessage> ContactMessages { get; set; }
        public DbSet<Wohnung> Wohnungen { get; set; }
        public DbSet<Wohnungsbild> Wohnungsbilder { get; set; }
        public DbSet<Amenity> Amenities { get; set; }
        public DbSet<Messages> Messages { get; set; }
        public DbSet<Favorite> Favorites { get; set; }
        public DbSet<RefreshToken> RefreshTokens { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            foreach (var entity in modelBuilder.Model.GetEntityTypes())
            {
                entity.SetTableName(entity.GetTableName()?.ToLower());

                foreach (var property in entity.GetProperties())
                {
                    property.SetColumnName(property.GetColumnBaseName().ToLower());
                }
            }

            modelBuilder.Entity<Favorite>()
                .HasKey(f => new { f.UserId, f.WohnungId });

            modelBuilder.Entity<Wohnung>()
                .HasMany(w => w.Bilder)
                .WithOne(b => b.Wohnung)
                .HasForeignKey(b => b.WohnungId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Wohnung>()
                .HasMany(w => w.Messages)
                .WithOne(m => m.Wohnung)
                .HasForeignKey(m => m.WohnungId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Benutzer>()
                .HasMany(u => u.Messages)
                .WithOne(m => m.User)
                .HasForeignKey(m => m.UserId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<Wohnung>()
                .Property(w => w.ExpiresAt)
                .HasDefaultValueSql("NOW() + INTERVAL '3 months'");

            modelBuilder.Entity<Wohnung>()
    .HasIndex(w => w.OwnerId)
    .HasDatabaseName("IX_Wohnung_OwnerId");

            modelBuilder.Entity<Wohnung>()
                .HasIndex(w => w.Stadt)
                .HasDatabaseName("IX_Wohnung_Stadt");

            modelBuilder.Entity<Wohnung>()
                .HasIndex(w => w.Status)
                .HasDatabaseName("IX_Wohnung_Status");

            modelBuilder.Entity<Wohnung>()
                .HasIndex(w => w.ExpiresAt)
                .HasDatabaseName("IX_Wohnung_ExpiresAt");

            // RefreshToken Indexes
            modelBuilder.Entity<RefreshToken>()
                .HasIndex(rt => rt.Token)
                .IsUnique()
                .HasDatabaseName("IX_RefreshToken_Token");
        }
    }
}