using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HudhudNestApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanListingLimit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ListingLimit",
                table: "Plans",
                type: "integer",
                nullable: true);

            // Data backfill (BACKEND-ISSUES.md §B-3): a database migrated from before this
            // column existed already has Free/Basic/Premium/Elite rows from PlansSeed, and
            // PlansSeed.EnsureAsync only inserts a tier that is missing — it will not touch
            // ListingLimit on a row that already exists. Without this, every plan in an
            // already-seeded environment would read back ListingLimit = NULL, which
            // ListingQuotaPolicy treats as "unlimited" — silently removing every quota
            // instead of applying the intended one. Values mirror
            // FRONTEND_BACKEND_CONTRACT.md §11.1 exactly as PlansSeed does for a fresh
            // database; Elite is left NULL (unlimited/negotiated) so no UPDATE is needed for it.
            migrationBuilder.Sql(
                "UPDATE \"Plans\" SET \"ListingLimit\" = 1 WHERE \"Tier\" = 'free';");
            migrationBuilder.Sql(
                "UPDATE \"Plans\" SET \"ListingLimit\" = 50 WHERE \"Tier\" = 'basic';");
            migrationBuilder.Sql(
                "UPDATE \"Plans\" SET \"ListingLimit\" = 250 WHERE \"Tier\" = 'premium';");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Plans_ListingLimit_PositiveOrUnlimited",
                table: "Plans",
                sql: "\"ListingLimit\" IS NULL OR \"ListingLimit\" > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Plans_ListingLimit_PositiveOrUnlimited",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "ListingLimit",
                table: "Plans");
        }
    }
}
