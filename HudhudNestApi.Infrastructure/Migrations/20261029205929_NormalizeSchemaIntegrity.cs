using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HudhudNestApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeSchemaIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VisitRequests_PropertyId",
                table: "VisitRequests");

            migrationBuilder.DropIndex(
                name: "IX_ValuationOfficeInvitations_AgencyId",
                table: "ValuationOfficeInvitations");

            migrationBuilder.DropIndex(
                name: "IX_UserRatings_RatedUserId",
                table: "UserRatings");

            migrationBuilder.DropIndex(
                name: "IX_UnitBookingRanges_UnitId",
                table: "UnitBookingRanges");

            migrationBuilder.DropIndex(
                name: "IX_SocialPublications_Status",
                table: "SocialPublications");

            migrationBuilder.DropIndex(
                name: "IX_SocialMediaAssets_PropertyId",
                table: "SocialMediaAssets");

            migrationBuilder.DropIndex(
                name: "IX_ServiceRequests_PropertyId",
                table: "ServiceRequests");

            migrationBuilder.DropIndex(
                name: "IX_ServiceRequests_Status",
                table: "ServiceRequests");

            migrationBuilder.DropIndex(
                name: "IX_ServiceOfferings_Category",
                table: "ServiceOfferings");

            migrationBuilder.DropIndex(
                name: "IX_RefreshTokens_UserId",
                table: "RefreshTokens");

            migrationBuilder.DropIndex(
                name: "IX_PropertyShareEvents_PropertyId",
                table: "PropertyShareEvents");

            migrationBuilder.DropIndex(
                name: "IX_PropertyReviews_PropertyId",
                table: "PropertyReviews");

            migrationBuilder.DropIndex(
                name: "IX_PropertyAttributionEvents_PropertyId",
                table: "PropertyAttributionEvents");

            migrationBuilder.DropIndex(
                name: "IX_Properties_CountryCode",
                table: "Properties");

            migrationBuilder.DropIndex(
                name: "IX_Properties_CountryCode_City",
                table: "Properties");

            migrationBuilder.DropIndex(
                name: "IX_Properties_GovernorateId",
                table: "Properties");

            migrationBuilder.DropIndex(
                name: "IX_Properties_Status",
                table: "Properties");

            migrationBuilder.DropIndex(
                name: "IX_Offers_Status",
                table: "Offers");

            migrationBuilder.DropIndex(
                name: "IX_Messages_SenderId",
                table: "Messages");

            migrationBuilder.DropIndex(
                name: "IX_MarketingEvents_EventType",
                table: "MarketingEvents");

            migrationBuilder.DropIndex(
                name: "IX_InvestmentWatchlistItems_UserId",
                table: "InvestmentWatchlistItems");

            migrationBuilder.DropIndex(
                name: "IX_InvestmentInterests_UserId",
                table: "InvestmentInterests");

            migrationBuilder.DropIndex(
                name: "IX_InvestmentDocuments_InvestmentProjectId",
                table: "InvestmentDocuments");

            migrationBuilder.DropIndex(
                name: "IX_Favorites_UserId",
                table: "Favorites");

            migrationBuilder.DropIndex(
                name: "IX_DistributionRules_IsActive",
                table: "DistributionRules");

            migrationBuilder.CreateIndex(
                name: "IX_ValuationInquiries_PropertyTypeId",
                table: "ValuationInquiries",
                column: "PropertyTypeId");

            AddCheckNotValid(migrationBuilder, "CK_UserRatings_Conduct", "UserRatings", "\"Conduct\" BETWEEN 1 AND 5");

            AddCheckNotValid(migrationBuilder, "CK_UserRatings_Credibility", "UserRatings", "\"Credibility\" BETWEEN 1 AND 5");

            AddCheckNotValid(migrationBuilder, "CK_UserRatings_InformationAccuracy", "UserRatings", "\"InformationAccuracy\" BETWEEN 1 AND 5");

            AddCheckNotValid(migrationBuilder, "CK_UserRatings_ResponseSpeed", "UserRatings", "\"ResponseSpeed\" BETWEEN 1 AND 5");

            AddCheckNotValid(migrationBuilder, "CK_UserRatings_Safety", "UserRatings", "\"Safety\" BETWEEN 1 AND 5");

            AddCheckNotValid(migrationBuilder, "CK_UserRatings_Transparency", "UserRatings", "\"Transparency\" BETWEEN 1 AND 5");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_CurrencyId",
                table: "Transactions",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_ShortStayReviews_ReviewerId",
                table: "ShortStayReviews",
                column: "ReviewerId");

            AddCheckNotValid(migrationBuilder, "CK_ShortStayReviews_Rating", "ShortStayReviews", "\"Rating\" BETWEEN 1 AND 5");

            migrationBuilder.CreateIndex(
                name: "IX_ShortStayListings_DistrictId",
                table: "ShortStayListings",
                column: "DistrictId");

            migrationBuilder.CreateIndex(
                name: "IX_ShortStayListings_GovernorateId",
                table: "ShortStayListings",
                column: "GovernorateId");

            migrationBuilder.CreateIndex(
                name: "IX_ShortStayListings_NeighborhoodId",
                table: "ShortStayListings",
                column: "NeighborhoodId");

            migrationBuilder.CreateIndex(
                name: "IX_ShortStayListings_PropertyId",
                table: "ShortStayListings",
                column: "PropertyId");

            AddCheckNotValid(migrationBuilder, "CK_ShortStayBookings_Amounts", "ShortStayBookings", "\"DepositAmount\" >= 0 AND \"DepositAmount\" <= \"TotalAmount\" AND \"RemainingAmount\" = \"TotalAmount\" - \"DepositAmount\"");

            AddCheckNotValid(migrationBuilder, "CK_ShortStayBookings_Dates", "ShortStayBookings", "\"CheckOut\" > \"CheckIn\"");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceReviews_ReviewerId",
                table: "ServiceReviews",
                column: "ReviewerId");

            AddCheckNotValid(migrationBuilder, "CK_ServiceReviews_Rating", "ServiceReviews", "\"Rating\" BETWEEN 1 AND 5");

            migrationBuilder.CreateIndex(
                name: "IX_SavedSearches_DistrictId",
                table: "SavedSearches",
                column: "DistrictId");

            migrationBuilder.CreateIndex(
                name: "IX_SavedSearches_GovernorateId",
                table: "SavedSearches",
                column: "GovernorateId");

            migrationBuilder.CreateIndex(
                name: "IX_SavedSearches_NeighborhoodId",
                table: "SavedSearches",
                column: "NeighborhoodId");

            migrationBuilder.CreateIndex(
                name: "IX_SavedSearches_PropertyTypeId",
                table: "SavedSearches",
                column: "PropertyTypeId");

            AddCheckNotValid(migrationBuilder, "CK_PropertyReviews_Rating", "PropertyReviews", "\"Rating\" BETWEEN 1 AND 5");

            AddForeignKeyNotValid(migrationBuilder, "FK_HostVerificationRecords_UserAccounts_UserId", "HostVerificationRecords", "UserId", "UserAccounts", "RESTRICT");

            AddForeignKeyNotValid(migrationBuilder, "FK_SavedSearches_Districts_DistrictId", "SavedSearches", "DistrictId", "Districts", "RESTRICT");

            AddForeignKeyNotValid(migrationBuilder, "FK_SavedSearches_Governorates_GovernorateId", "SavedSearches", "GovernorateId", "Governorates", "RESTRICT");

            AddForeignKeyNotValid(migrationBuilder, "FK_SavedSearches_Neighborhoods_NeighborhoodId", "SavedSearches", "NeighborhoodId", "Neighborhoods", "RESTRICT");

            AddForeignKeyNotValid(migrationBuilder, "FK_SavedSearches_PropertyTypes_PropertyTypeId", "SavedSearches", "PropertyTypeId", "PropertyTypes", "RESTRICT");

            AddForeignKeyNotValid(migrationBuilder, "FK_ServiceProviders_Agencies_AgencyId", "ServiceProviders", "AgencyId", "Agencies", "SET NULL");

            AddForeignKeyNotValid(migrationBuilder, "FK_ServiceReviews_UserAccounts_ReviewerId", "ServiceReviews", "ReviewerId", "UserAccounts", "RESTRICT");

            AddForeignKeyNotValid(migrationBuilder, "FK_ShortStayBookings_UserAccounts_GuestId", "ShortStayBookings", "GuestId", "UserAccounts", "RESTRICT");

            AddForeignKeyNotValid(migrationBuilder, "FK_ShortStayListings_Districts_DistrictId", "ShortStayListings", "DistrictId", "Districts", "RESTRICT");

            AddForeignKeyNotValid(migrationBuilder, "FK_ShortStayListings_Governorates_GovernorateId", "ShortStayListings", "GovernorateId", "Governorates", "RESTRICT");

            AddForeignKeyNotValid(migrationBuilder, "FK_ShortStayListings_Neighborhoods_NeighborhoodId", "ShortStayListings", "NeighborhoodId", "Neighborhoods", "RESTRICT");

            AddForeignKeyNotValid(migrationBuilder, "FK_ShortStayListings_Properties_PropertyId", "ShortStayListings", "PropertyId", "Properties", "SET NULL");

            AddForeignKeyNotValid(migrationBuilder, "FK_ShortStayListings_UserAccounts_OwnerId", "ShortStayListings", "OwnerId", "UserAccounts", "RESTRICT");

            AddForeignKeyNotValid(migrationBuilder, "FK_ShortStayReviews_ShortStayBookings_BookingId", "ShortStayReviews", "BookingId", "ShortStayBookings", "RESTRICT");

            AddForeignKeyNotValid(migrationBuilder, "FK_ShortStayReviews_ShortStayListings_ShortStayListingId", "ShortStayReviews", "ShortStayListingId", "ShortStayListings", "RESTRICT");

            AddForeignKeyNotValid(migrationBuilder, "FK_ShortStayReviews_UserAccounts_ReviewerId", "ShortStayReviews", "ReviewerId", "UserAccounts", "RESTRICT");

            AddForeignKeyNotValid(migrationBuilder, "FK_Transactions_Currencies_CurrencyId", "Transactions", "CurrencyId", "Currencies", "RESTRICT");

            AddForeignKeyNotValid(migrationBuilder, "FK_ValuationInquiries_PropertyTypes_PropertyTypeId", "ValuationInquiries", "PropertyTypeId", "PropertyTypes", "RESTRICT");
        }

        // New keys and checks are added NOT VALID: PostgreSQL enforces them for every new or
        // changed row at once, without scanning or locking the table for a full check. Each one is
        // then validated against the existing rows inside its own sub-transaction; if old rows break
        // it, the migration does NOT fail and no row is touched -- the constraint stays NOT VALID,
        // a WARNING names it, and scripts/database/normalize-schema-integrity-check.sql lists the rows to fix
        // before running VALIDATE CONSTRAINT by hand.
        private static void AddForeignKeyNotValid(
            MigrationBuilder migrationBuilder, string name, string table, string column, string principalTable, string onDelete)
        {
            migrationBuilder.Sql(
                $"ALTER TABLE \"{table}\" ADD CONSTRAINT \"{name}\" FOREIGN KEY (\"{column}\") " +
                $"REFERENCES \"{principalTable}\" (\"Id\") ON DELETE {onDelete} NOT VALID;");
            ValidateOrWarn(migrationBuilder, name, table);
        }

        private static void AddCheckNotValid(MigrationBuilder migrationBuilder, string name, string table, string sql)
        {
            migrationBuilder.Sql($"ALTER TABLE \"{table}\" ADD CONSTRAINT \"{name}\" CHECK ({sql}) NOT VALID;");
            ValidateOrWarn(migrationBuilder, name, table);
        }

        private static void ValidateOrWarn(MigrationBuilder migrationBuilder, string name, string table)
        {
            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    ALTER TABLE "{table}" VALIDATE CONSTRAINT "{name}";
                EXCEPTION WHEN foreign_key_violation OR check_violation THEN
                    RAISE WARNING 'Constraint % on % left NOT VALID: existing rows break it (see scripts/database/normalize-schema-integrity-check.sql).', '{name}', '{table}';
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_HostVerificationRecords_UserAccounts_UserId",
                table: "HostVerificationRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_SavedSearches_Districts_DistrictId",
                table: "SavedSearches");

            migrationBuilder.DropForeignKey(
                name: "FK_SavedSearches_Governorates_GovernorateId",
                table: "SavedSearches");

            migrationBuilder.DropForeignKey(
                name: "FK_SavedSearches_Neighborhoods_NeighborhoodId",
                table: "SavedSearches");

            migrationBuilder.DropForeignKey(
                name: "FK_SavedSearches_PropertyTypes_PropertyTypeId",
                table: "SavedSearches");

            migrationBuilder.DropForeignKey(
                name: "FK_ServiceProviders_Agencies_AgencyId",
                table: "ServiceProviders");

            migrationBuilder.DropForeignKey(
                name: "FK_ServiceReviews_UserAccounts_ReviewerId",
                table: "ServiceReviews");

            migrationBuilder.DropForeignKey(
                name: "FK_ShortStayBookings_UserAccounts_GuestId",
                table: "ShortStayBookings");

            migrationBuilder.DropForeignKey(
                name: "FK_ShortStayListings_Districts_DistrictId",
                table: "ShortStayListings");

            migrationBuilder.DropForeignKey(
                name: "FK_ShortStayListings_Governorates_GovernorateId",
                table: "ShortStayListings");

            migrationBuilder.DropForeignKey(
                name: "FK_ShortStayListings_Neighborhoods_NeighborhoodId",
                table: "ShortStayListings");

            migrationBuilder.DropForeignKey(
                name: "FK_ShortStayListings_Properties_PropertyId",
                table: "ShortStayListings");

            migrationBuilder.DropForeignKey(
                name: "FK_ShortStayListings_UserAccounts_OwnerId",
                table: "ShortStayListings");

            migrationBuilder.DropForeignKey(
                name: "FK_ShortStayReviews_ShortStayBookings_BookingId",
                table: "ShortStayReviews");

            migrationBuilder.DropForeignKey(
                name: "FK_ShortStayReviews_ShortStayListings_ShortStayListingId",
                table: "ShortStayReviews");

            migrationBuilder.DropForeignKey(
                name: "FK_ShortStayReviews_UserAccounts_ReviewerId",
                table: "ShortStayReviews");

            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_Currencies_CurrencyId",
                table: "Transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_ValuationInquiries_PropertyTypes_PropertyTypeId",
                table: "ValuationInquiries");

            migrationBuilder.DropIndex(
                name: "IX_ValuationInquiries_PropertyTypeId",
                table: "ValuationInquiries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UserRatings_Conduct",
                table: "UserRatings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UserRatings_Credibility",
                table: "UserRatings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UserRatings_InformationAccuracy",
                table: "UserRatings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UserRatings_ResponseSpeed",
                table: "UserRatings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UserRatings_Safety",
                table: "UserRatings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UserRatings_Transparency",
                table: "UserRatings");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_CurrencyId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_ShortStayReviews_ReviewerId",
                table: "ShortStayReviews");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ShortStayReviews_Rating",
                table: "ShortStayReviews");

            migrationBuilder.DropIndex(
                name: "IX_ShortStayListings_DistrictId",
                table: "ShortStayListings");

            migrationBuilder.DropIndex(
                name: "IX_ShortStayListings_GovernorateId",
                table: "ShortStayListings");

            migrationBuilder.DropIndex(
                name: "IX_ShortStayListings_NeighborhoodId",
                table: "ShortStayListings");

            migrationBuilder.DropIndex(
                name: "IX_ShortStayListings_PropertyId",
                table: "ShortStayListings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ShortStayBookings_Amounts",
                table: "ShortStayBookings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ShortStayBookings_Dates",
                table: "ShortStayBookings");

            migrationBuilder.DropIndex(
                name: "IX_ServiceReviews_ReviewerId",
                table: "ServiceReviews");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ServiceReviews_Rating",
                table: "ServiceReviews");

            migrationBuilder.DropIndex(
                name: "IX_SavedSearches_DistrictId",
                table: "SavedSearches");

            migrationBuilder.DropIndex(
                name: "IX_SavedSearches_GovernorateId",
                table: "SavedSearches");

            migrationBuilder.DropIndex(
                name: "IX_SavedSearches_NeighborhoodId",
                table: "SavedSearches");

            migrationBuilder.DropIndex(
                name: "IX_SavedSearches_PropertyTypeId",
                table: "SavedSearches");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PropertyReviews_Rating",
                table: "PropertyReviews");

            migrationBuilder.CreateIndex(
                name: "IX_VisitRequests_PropertyId",
                table: "VisitRequests",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_ValuationOfficeInvitations_AgencyId",
                table: "ValuationOfficeInvitations",
                column: "AgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRatings_RatedUserId",
                table: "UserRatings",
                column: "RatedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UnitBookingRanges_UnitId",
                table: "UnitBookingRanges",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_SocialPublications_Status",
                table: "SocialPublications",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SocialMediaAssets_PropertyId",
                table: "SocialMediaAssets",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRequests_PropertyId",
                table: "ServiceRequests",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRequests_Status",
                table: "ServiceRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceOfferings_Category",
                table: "ServiceOfferings",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_UserId",
                table: "RefreshTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PropertyShareEvents_PropertyId",
                table: "PropertyShareEvents",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_PropertyReviews_PropertyId",
                table: "PropertyReviews",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_PropertyAttributionEvents_PropertyId",
                table: "PropertyAttributionEvents",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_Properties_CountryCode",
                table: "Properties",
                column: "CountryCode");

            migrationBuilder.CreateIndex(
                name: "IX_Properties_CountryCode_City",
                table: "Properties",
                columns: new[] { "CountryCode", "City" });

            migrationBuilder.CreateIndex(
                name: "IX_Properties_GovernorateId",
                table: "Properties",
                column: "GovernorateId");

            migrationBuilder.CreateIndex(
                name: "IX_Properties_Status",
                table: "Properties",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Offers_Status",
                table: "Offers",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_SenderId",
                table: "Messages",
                column: "SenderId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketingEvents_EventType",
                table: "MarketingEvents",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentWatchlistItems_UserId",
                table: "InvestmentWatchlistItems",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentInterests_UserId",
                table: "InvestmentInterests",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentDocuments_InvestmentProjectId",
                table: "InvestmentDocuments",
                column: "InvestmentProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Favorites_UserId",
                table: "Favorites",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_DistributionRules_IsActive",
                table: "DistributionRules",
                column: "IsActive");
        }
    }
}
