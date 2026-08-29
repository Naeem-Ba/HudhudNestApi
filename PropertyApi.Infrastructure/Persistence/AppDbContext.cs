using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using PropertyApi.Domain.Agencies.Entities;
using PropertyApi.Domain.Audit.Entities;
using PropertyApi.Domain.Listings.Entities;
using PropertyApi.Domain.Messaging.Entities;
using PropertyApi.Domain.Plans.Entities;
using PropertyApi.Domain.Users.Entities;
using IdentityApplicationRole = PropertyApi.Infrastructure.Identity.Entities.ApplicationRole;
using PropertyApi.Domain.Auth.Entities;
using PropertyApi.Domain.Notifications.Entities;
using PropertyApi.Domain.Bookings.Entities;
using PropertyApi.Domain.Reviews.Entities;
using PropertyApi.Domain.Lookups.Entities;
using PropertyApi.Domain.Transactions.Entities;
using PropertyApi.Domain.Search.Entities;
using PropertyApi.Domain.Services.Entities;
using PropertyApi.Infrastructure.Identity.Entities;
using PropertyApi.Infrastructure.Security.DataProtection;
using PropertyApi.Domain.ShortStay.Entities;



namespace PropertyApi.Infrastructure.Persistence;

/// <summary>
/// Main database context.
/// Inherits IdentityDbContext with Guid PKs for User and Role.
/// Auto-stamps UpdatedAt on SaveChanges.
/// Global soft-delete filter applied to all BaseEntity types.
/// </summary>
public sealed class AppDbContext
    : IdentityDbContext<ApplicationUser, IdentityApplicationRole, Guid>
{
    private readonly IDataProtectionProvider _dataProtectionProvider;

    public AppDbContext(
        DbContextOptions<AppDbContext> options,
        IDataProtectionProvider dataProtectionProvider)
        : base(options)
    {
        _dataProtectionProvider = dataProtectionProvider;
    }

    public AppDbContext(DbContextOptions<AppDbContext> options)
        : this(options, new EphemeralDataProtectionProvider())
    {
    }

    // -- DbSets --------------------------------------------------
    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();
    public DbSet<Agency> Agencies => Set<Agency>();
    public DbSet<AgencyInvitation> AgencyInvitations => Set<AgencyInvitation>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<Property> Properties => Set<Property>();
    public DbSet<PropertyPriceHistory> PropertyPriceHistories => Set<PropertyPriceHistory>();
    public DbSet<PropertyImage> PropertyImages => Set<PropertyImage>();
    public DbSet<Amenity> Amenities => Set<Amenity>();
    public DbSet<PropertyAmenity> PropertyAmenities => Set<PropertyAmenity>();
    public DbSet<Favorite> Favorites => Set<Favorite>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<OtpCode> OtpCodes => Set<OtpCode>();
    public DbSet<PhoneOtpChallenge> PhoneOtpChallenges => Set<PhoneOtpChallenge>();
    public DbSet<ContactMessage> ContactMessages => Set<ContactMessage>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<VisitRequest> VisitRequests => Set<VisitRequest>();
    public DbSet<PropertyReview> PropertyReviews => Set<PropertyReview>();
    public DbSet<UserRating> UserRatings => Set<UserRating>();
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<Governorate> Governorates => Set<Governorate>();
    public DbSet<District> Districts => Set<District>();
    public DbSet<Neighborhood> Neighborhoods => Set<Neighborhood>();
    public DbSet<PropertyType> PropertyTypes => Set<PropertyType>();
    public DbSet<LocationSuggestion> LocationSuggestions => Set<LocationSuggestion>();
    public DbSet<SaleDetails> SaleDetails => Set<SaleDetails>();
    public DbSet<RentalDetails> RentalDetails => Set<RentalDetails>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<SavedSearch> SavedSearches => Set<SavedSearch>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    // AqarTech Services Marketplace
    public DbSet<ServiceProvider> ServiceProviders => Set<ServiceProvider>();
    public DbSet<ServiceOffering> ServiceOfferings => Set<ServiceOffering>();
    public DbSet<ServiceRequest> ServiceRequests => Set<ServiceRequest>();
    public DbSet<ServiceRequestStatusHistory> ServiceRequestStatusHistories => Set<ServiceRequestStatusHistory>();
    public DbSet<ServiceReviewDocument> ServiceReviewDocuments => Set<ServiceReviewDocument>();
    public DbSet<ServiceReview> ServiceReviews => Set<ServiceReview>();

    // Short-Stay Accommodation subsystem
    public DbSet<AccommodationType> AccommodationTypes => Set<AccommodationType>();
    public DbSet<ShortStayListing> ShortStayListings => Set<ShortStayListing>();
    public DbSet<RoomType> ShortStayRoomTypes => Set<RoomType>();
    public DbSet<AccommodationUnit> AccommodationUnits => Set<AccommodationUnit>();
    public DbSet<PricingRule> ShortStayPricingRules => Set<PricingRule>();
    public DbSet<MinimumStayRule> ShortStayMinimumStayRules => Set<MinimumStayRule>();
    public DbSet<UnitBookingRange> UnitBookingRanges => Set<UnitBookingRange>();
    public DbSet<ShortStayListingAmenity> ShortStayListingAmenities => Set<ShortStayListingAmenity>();
    public DbSet<ShortStayListingPhoto> ShortStayListingPhotos => Set<ShortStayListingPhoto>();
    public DbSet<Booking> ShortStayBookings => Set<Booking>();
    public DbSet<ShortStayReview> ShortStayReviews => Set<ShortStayReview>();
    public DbSet<HostVerificationRecord> HostVerificationRecords => Set<HostVerificationRecord>();

    // -- Model Configuration -------------------------------------
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder); // ? REQUIRED: configures Identity tables

        // Load all IEntityTypeConfiguration<T> classes from this assembly
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // -- Rename Identity tables to clean English names -------
        builder.Entity<ApplicationUser>().ToTable("Users");
        builder.Entity<IdentityApplicationRole>().ToTable("Roles");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("UserRoles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("UserClaims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("UserLogins");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("RoleClaims");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("UserTokens");

        ConfigureEncryptedUserFields(builder);
        ConfigureEncryptedUserAccountFields(builder);

        // -- Global soft-delete filter ---------------------------
        // Automatically excludes IsDeleted=true from ALL queries.
        // Override with .IgnoreQueryFilters() when you need deleted records.
        builder.Entity<Property>().HasQueryFilter(e => !e.IsDeleted);
        builder.Entity<PropertyImage>().HasQueryFilter(e => !e.IsDeleted);
        builder.Entity<Amenity>().HasQueryFilter(e => !e.IsDeleted);
        builder.Entity<Message>().HasQueryFilter(e => !e.IsDeleted);
        builder.Entity<ContactMessage>().HasQueryFilter(e => !e.IsDeleted);
        builder.Entity<ApplicationUser>().HasQueryFilter(e => !e.IsDeleted);
        builder.Entity<Notification>().HasQueryFilter(n => !n.IsDeleted);
        builder.Entity<VisitRequest>().HasQueryFilter(v => !v.IsDeleted);
        builder.Entity<PropertyReview>().HasQueryFilter(r => !r.IsDeleted);
        builder.Entity<UserRating>().HasQueryFilter(r => !r.IsDeleted);
        builder.Entity<Transaction>().HasQueryFilter(e => !e.IsDeleted);

        builder.Entity<Favorite>().HasQueryFilter(favorite => !favorite.Property.IsDeleted);

        builder.Entity<PropertyAmenity>().HasQueryFilter(propertyAmenity => !propertyAmenity.Property.IsDeleted && !propertyAmenity.Amenity.IsDeleted);

        builder.Entity<RefreshToken>().HasQueryFilter(refreshToken => !refreshToken.User.IsDeleted);

        builder.Entity<DataProtectionKey>().ToTable("DataProtectionKeys");

        // AqarTech Services Marketplace
        builder.Entity<ServiceProvider>().HasQueryFilter(e => !e.IsDeleted);
        builder.Entity<ServiceOffering>().HasQueryFilter(e => !e.IsDeleted);
        builder.Entity<ServiceRequest>().HasQueryFilter(e => !e.IsDeleted);
        builder.Entity<ServiceRequestStatusHistory>().HasQueryFilter(e => !e.IsDeleted);
        builder.Entity<ServiceReviewDocument>().HasQueryFilter(e => !e.IsDeleted);
        builder.Entity<ServiceReview>().HasQueryFilter(e => !e.IsDeleted);
    }


    private void ConfigureEncryptedUserFields(ModelBuilder builder)
    {
        var user = builder.Entity<ApplicationUser>();

        user.Property(u => u.PhoneNumber)
            .HasMaxLength(1024)
            .HasConversion(new DataProtectionStringConverter(
                _dataProtectionProvider,
                SensitiveDataProtectionPurposes.UserPhoneNumber));

        user.Property(
                u => u.PhoneNumberLookupHash)
            .HasMaxLength(64);

        user.HasIndex(
                u => u.PhoneNumberLookupHash)
            .IsUnique();

    }

    private void ConfigureEncryptedUserAccountFields(
    ModelBuilder builder)
    {
        var account = builder.Entity<UserAccount>();

        account.Property(u => u.WhatsAppNumber)
            .HasMaxLength(1024)
            .HasConversion(new DataProtectionStringConverter(
                _dataProtectionProvider,
                SensitiveDataProtectionPurposes.UserWhatsAppNumber));

        account.Property(u => u.TaxNumber)
            .HasMaxLength(1024)
            .HasConversion(new DataProtectionStringConverter(
                _dataProtectionProvider,
                SensitiveDataProtectionPurposes.UserTaxNumber));

        // ✅ جديد — نبذة تعريفية + عنوان/تواصل بالملف الشخصي العام. غير مشفَّرة
        // عمداً (بعكس WhatsAppNumber/TaxNumber أعلاه): المستخدم يكتبها بنفسه
        // ليعرضها علنًا لأي زائر، فلا معنى لتشفيرها في قاعدة البيانات.
        account.Property(u => u.Bio)
            .HasMaxLength(2000);

        account.Property(u => u.ContactInfo)
            .HasMaxLength(500);

        // ✅ جديد — يخزّن Cloudinary PublicId لصورة الحساب الحالية كي نستطيع
        // حذفها من التخزين السحابي عند رفع صورة جديدة (نفس نمط PropertyImage.PublicId).
        // غير مشفَّر: ليس بيانًا حساسًا، وهو معرّف داخلي لمزوّد التخزين فقط.
        account.Property(u => u.ProfileImagePublicId)
            .HasMaxLength(300);
    }
    // -- Auto-stamp UpdatedAt on every save ----------------------
    public override async Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries())
        {
            // Stamp UpdatedAt on any modified entity that has the property
            if (entry.State == EntityState.Modified)
            {
                var updatedAt = entry.Properties
                    .FirstOrDefault(p => p.Metadata.Name == "UpdatedAt");
                if (updatedAt is not null)
                    updatedAt.CurrentValue = now;
            }

            // Intercept hard-delete and convert to soft-delete
            if (entry.State == EntityState.Deleted)
            {
                var isDeleted = entry.Properties
                    .FirstOrDefault(p => p.Metadata.Name == "IsDeleted");
                if (isDeleted is not null)
                {
                    entry.State = EntityState.Modified;
                    isDeleted.CurrentValue = true;

                    var deletedAt = entry.Properties
                        .FirstOrDefault(p => p.Metadata.Name == "DeletedAt");
                    if (deletedAt is not null)
                        deletedAt.CurrentValue = now;
                }
            }
        }

        return await base.SaveChangesAsync(cancellationToken);
    }
}

