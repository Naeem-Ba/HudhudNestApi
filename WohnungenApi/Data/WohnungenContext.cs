using Microsoft.EntityFrameworkCore;
using WohnungenApi.Models;
using WohnungenApi.Models.Enums;

namespace WohnungenApi.Data
{
    public class WohnungenContext : DbContext
    {
        public WohnungenContext(DbContextOptions<WohnungenContext> options)
            : base(options)
        {
            // حل مشكلة التواريخ في PostgreSQL
            AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        }

        public DbSet<ContactMessage> ContactMessages { get; set; }
        public DbSet<Wohnung> Wohnungen { get; set; }
        public DbSet<Wohnungsbild> Wohnungsbilder { get; set; }
        public DbSet<Amenity> Amenities { get; set; }
        public DbSet<Messages> Messages { get; set; }
        public DbSet<Benutzer> Benutzer { get; set; }
        public DbSet<Favorite> Favorites { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            // أضف هذا السطر لتوحيد حالة الأحرف للصغير في PostgreSQL
            //modelBuilder.HasDefaultSchema("public");

            foreach (var entity in modelBuilder.Model.GetEntityTypes())
            {
                // تحويل أسماء الجداول للأحرف الصغيرة
                entity.SetTableName(entity.GetTableName()?.ToLower());

                // تحويل أسماء الأعمدة للأحرف الصغيرة
                foreach (var property in entity.GetProperties())
                {
                    var columnName = property.GetColumnBaseName();
                    property.SetColumnName(columnName.ToLower());
                }
            }

            // Favorite: Composite Key
            modelBuilder.Entity<Favorite>()
                .HasKey(f => new { f.UserId, f.WohnungId });

            // Wohnung - Amenity Many-to-Many
            modelBuilder.Entity<Wohnung>()
                .HasMany(w => w.Amenities)
                .WithMany(a => a.Wohnungen)
                .UsingEntity(j => j.ToTable("wohnungsamenities"));

            // Beziehungen
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


            modelBuilder.Entity<Wohnung>(entity =>
            {
                entity.Property(e => e.Kaltmiete).HasPrecision(18, 2);
                entity.Property(e => e.Kaufpreis).HasPrecision(18, 2);
                entity.Property(e => e.Kaution).HasPrecision(18, 2);
                entity.Property(e => e.Nebenkosten).HasPrecision(18, 2);
                entity.Property(e => e.Warmmiete).HasPrecision(18, 2);
            });
        }

    }
}