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
using PropertyApi.Domain.Investments.Entities;
using PropertyApi.Domain.AppUpdates.Entities;
using PropertyApi.Domain.Marketing.Entities;
using PropertyApi.Domain.SocialDistribution.Entities;
using PropertyApi.Domain.Valuation.Entities;



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
    public DbSet<PhoneOtpChallenge> PhoneOtpChallenges => Set<PhoneOtpChallenge>();
    public DbSet<ContactMessage> ContactMessages => Set<ContactMessage>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<Offer> Offers => Set<Offer>();
    public DbSet<SurveyResponse> SurveyResponses => Set<SurveyResponse>();
    public DbSet<MarketingEvent> MarketingEvents => Set<MarketingEvent>();
    public DbSet<PropertyShareEvent> PropertyShareEvents => Set<PropertyShareEvent>();
    public DbSet<PropertyAttributionEvent> PropertyAttributionEvents => Set<PropertyAttributionEvent>();
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
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<SavedSearch> SavedSearches => Set<SavedSearch>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    public DbSet<ConsentRecord> ConsentRecords => Set<ConsentRecord>();

    // HudhudNest Services Marketplace
    public DbSet<ServiceProvider> ServiceProviders => Set<ServiceProvider>();
    public DbSet<ServiceOffering> ServiceOfferings => Set<ServiceOffering>();
    public DbSet<ServiceRequest> ServiceRequests => Set<ServiceRequest>();
    public DbSet<ServiceRequestStatusHistory> ServiceRequestStatusHistories => Set<ServiceRequestStatusHistory>();
    public DbSet<ServiceRequestDocument> ServiceRequestDocuments => Set<ServiceRequestDocument>();
    public DbSet<ServiceReview> ServiceReviews => Set<ServiceReview>();

    // Social Distribution Domain (Phase 3)
    public DbSet<SocialChannel> SocialChannels => Set<SocialChannel>();
    public DbSet<SocialAccount> SocialAccounts => Set<SocialAccount>();
    public DbSet<SocialPublication> SocialPublications => Set<SocialPublication>();
    public DbSet<SocialPostContent> SocialPostContents => Set<SocialPostContent>();
    public DbSet<SocialPublicationStatusHistory> SocialPublicationStatusHistories => Set<SocialPublicationStatusHistory>();

    // Provinces & Distribution Rules (Phase 4)
    public DbSet<DistributionRule> DistributionRules => Set<DistributionRule>();
    public DbSet<DistributionRun> DistributionRuns => Set<DistributionRun>();

    // Queue + Background Workers (Phase 6) / Social Media Asset Generation (Phase 7)
    public DbSet<SocialPublicationDeadLetter> SocialPublicationDeadLetters => Set<SocialPublicationDeadLetter>();
    public DbSet<SocialMediaAsset> SocialMediaAssets => Set<SocialMediaAsset>();

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

    // App Update Management (Phase 1)
    public DbSet<AppRelease> AppReleases => Set<AppRelease>();

    // Investment Discovery module (Phase 1)
    public DbSet<InvestmentProject> InvestmentProjects => Set<InvestmentProject>();
    public DbSet<InvestmentProjectFinancials> InvestmentProjectFinancials => Set<InvestmentProjectFinancials>();
    public DbSet<InvestmentRiskAssessment> InvestmentRiskAssessments => Set<InvestmentRiskAssessment>();
    public DbSet<InvestmentDocument> InvestmentDocuments => Set<InvestmentDocument>();
    public DbSet<InvestmentUpdate> InvestmentUpdates => Set<InvestmentUpdate>();
    public DbSet<InvestmentWatchlistItem> InvestmentWatchlistItems => Set<InvestmentWatchlistItem>();
    public DbSet<InvestmentInterest> InvestmentInterests => Set<InvestmentInterest>();

    // Valuation module (Stage 2 domain, Stage 5+6 persistence, Stage 9 consent).
    public DbSet<ValuationInquiry> ValuationInquiries => Set<ValuationInquiry>();
    public DbSet<ValuationOfficeInvitation> ValuationOfficeInvitations => Set<ValuationOfficeInvitation>();
    public DbSet<ValuationOfficeResponse> ValuationOfficeResponses => Set<ValuationOfficeResponse>();
    public DbSet<ValuationContactConsent> ValuationContactConsents => Set<ValuationContactConsent>();

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
        ConfigureEncryptedSocialAccountFields(builder);

        // -- Global soft-delete filter ---------------------------
        // Automatically excludes IsDeleted=true from ALL queries.
        // Override with .IgnoreQueryFilters() when you need deleted records.
        builder.Entity<Property>().HasQueryFilter(e => !e.IsDeleted);
        builder.Entity<PropertyImage>().HasQueryFilter(e => !e.IsDeleted);
        builder.Entity<Amenity>().HasQueryFilter(e => !e.IsDeleted);
        builder.Entity<Message>().HasQueryFilter(e => !e.IsDeleted);
        builder.Entity<ContactMessage>().HasQueryFilter(e => !e.IsDeleted);
        builder.Entity<Lead>().HasQueryFilter(e => !e.IsDeleted);
        builder.Entity<Offer>().HasQueryFilter(e => !e.IsDeleted);
        builder.Entity<SurveyResponse>().HasQueryFilter(e => !e.IsDeleted);
        builder.Entity<MarketingEvent>().HasQueryFilter(e => !e.IsDeleted);
        builder.Entity<PropertyShareEvent>().HasQueryFilter(e => !e.IsDeleted);
        builder.Entity<PropertyAttributionEvent>().HasQueryFilter(e => !e.IsDeleted);
        builder.Entity<ApplicationUser>().HasQueryFilter(e => !e.IsDeleted);
        builder.Entity<Notification>().HasQueryFilter(n => !n.IsDeleted);
        builder.Entity<VisitRequest>().HasQueryFilter(v => !v.IsDeleted);
        builder.Entity<PropertyReview>().HasQueryFilter(r => !r.IsDeleted);
        builder.Entity<UserRating>().HasQueryFilter(r => !r.IsDeleted);
        builder.Entity<Transaction>().HasQueryFilter(e => !e.IsDeleted);

        // Short-Stay Accommodation: added alongside the plan-quota fix (a deleted listing
        // must stop being reachable/countable the same way a deleted Property already is —
        // see IActiveListingCounter).
        builder.Entity<ShortStayListing>().HasQueryFilter(e => !e.IsDeleted);

        builder.Entity<Favorite>().HasQueryFilter(favorite => !favorite.Property.IsDeleted);

        builder.Entity<PropertyAmenity>().HasQueryFilter(propertyAmenity => !propertyAmenity.Property.IsDeleted && !propertyAmenity.Amenity.IsDeleted);

        builder.Entity<RefreshToken>().HasQueryFilter(refreshToken => !refreshToken.User.IsDeleted);

        builder.Entity<DataProtectionKey>().ToTable("DataProtectionKeys");

        // HudhudNest Services Marketplace
        builder.Entity<ServiceProvider>().HasQueryFilter(e => !e.IsDeleted);
        builder.Entity<ServiceOffering>().HasQueryFilter(e => !e.IsDeleted);
        builder.Entity<ServiceRequest>().HasQueryFilter(e => !e.IsDeleted);
        builder.Entity<ServiceRequestStatusHistory>().HasQueryFilter(e => !e.IsDeleted);
        builder.Entity<ServiceRequestDocument>().HasQueryFilter(e => !e.IsDeleted);
        builder.Entity<ServiceReview>().HasQueryFilter(e => !e.IsDeleted);

        // Social Distribution Domain (Phase 3)
        builder.Entity<SocialChannel>().HasQueryFilter(e => !e.IsDeleted);
        builder.Entity<SocialAccount>().HasQueryFilter(e => !e.IsDeleted);
        builder.Entity<SocialPublication>().HasQueryFilter(e => !e.IsDeleted);
        builder.Entity<SocialPostContent>().HasQueryFilter(e => !e.IsDeleted);
        builder.Entity<SocialPublicationStatusHistory>().HasQueryFilter(e => !e.IsDeleted);
        builder.Entity<DistributionRule>().HasQueryFilter(e => !e.IsDeleted);
        builder.Entity<DistributionRun>().HasQueryFilter(e => !e.IsDeleted);
        builder.Entity<SocialPublicationDeadLetter>().HasQueryFilter(e => !e.IsDeleted);
        builder.Entity<SocialMediaAsset>().HasQueryFilter(e => !e.IsDeleted);
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

    /// <summary>
    /// SocialAccount.CredentialReference is encrypted at rest even though it is only ever meant
    /// to hold an opaque secret-manager reference, never a raw token (see SocialAccount's
    /// remarks) — defense in depth for the day an operator pastes something more sensitive by
    /// mistake. Same converter/pattern as WhatsAppNumber/TaxNumber above.
    /// </summary>
    private void ConfigureEncryptedSocialAccountFields(ModelBuilder builder)
    {
        builder.Entity<PropertyApi.Domain.SocialDistribution.Entities.SocialAccount>()
            .Property(a => a.CredentialReference)
            .HasMaxLength(1024)
            .HasConversion(new DataProtectionStringConverter(
                _dataProtectionProvider,
                SensitiveDataProtectionPurposes.SocialAccountCredentialReference));
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

