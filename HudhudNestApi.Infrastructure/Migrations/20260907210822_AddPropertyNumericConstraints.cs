using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HudhudNestApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Finding F4 (docs/DATABASE-PRODUCTION-READINESS.md): before this migration, the only
    /// application-level CHECK constraint in the entire schema was
    /// CK_Plans_ListingLimit_PositiveOrUnlimited -- Area, Rooms, and the three price columns
    /// relied solely on application validation, with no database-level backstop against a
    /// negative or zero value reaching the table directly.
    ///
    /// Every one of the five columns below stays nullable: Area is legitimately optional
    /// except for Land listings (CreatePropertyCommandValidator.cs), and ColdRent/WarmRent/
    /// PurchasePrice are legitimately null depending on ListingType (a for-sale listing has no
    /// monthly rent, and vice versa). "No NULL" was resolved, with the project owner, to mean
    /// "no zero/negative placeholder value", not literal column nullability -- each constraint
    /// below is exactly `col IS NULL OR col > 0`. Declared in PropertyConfiguration.cs's
    /// ToTable(...) call, not only here, so the EF model and applied schema stay in sync.
    ///
    /// Run scripts/database/audit-f4-numeric-constraints.sql against any environment before
    /// applying this migration there. Postgres refuses to add a CHECK constraint any existing
    /// row already violates, so this migration itself is the final, authoritative check on top
    /// of that audit script -- it modifies no row and fails the whole transaction atomically if
    /// any offending value exists, rather than silently correcting or deleting it.
    /// </summary>
    public partial class AddPropertyNumericConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_Properties_Area_PositiveOrNull",
                table: "Properties",
                sql: "\"Area\" IS NULL OR \"Area\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Properties_ColdRent_PositiveOrNull",
                table: "Properties",
                sql: "\"ColdRent\" IS NULL OR \"ColdRent\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Properties_PurchasePrice_PositiveOrNull",
                table: "Properties",
                sql: "\"PurchasePrice\" IS NULL OR \"PurchasePrice\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Properties_Rooms_PositiveOrNull",
                table: "Properties",
                sql: "\"Rooms\" IS NULL OR \"Rooms\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Properties_WarmRent_PositiveOrNull",
                table: "Properties",
                sql: "\"WarmRent\" IS NULL OR \"WarmRent\" > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Properties_Area_PositiveOrNull",
                table: "Properties");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Properties_ColdRent_PositiveOrNull",
                table: "Properties");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Properties_PurchasePrice_PositiveOrNull",
                table: "Properties");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Properties_Rooms_PositiveOrNull",
                table: "Properties");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Properties_WarmRent_PositiveOrNull",
                table: "Properties");
        }
    }
}
