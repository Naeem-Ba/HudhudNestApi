using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HudhudNestApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LaunchAuditOpenVisitAndLiveReviewIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PropertyReviews_PropertyId_ReviewerId",
                table: "PropertyReviews");

            // Launch audit A2-002: duplicate open visit requests could exist from the race this
            // index closes. Keep the oldest open request per (property, requester) and cancel the
            // rest (Status 3 = Cancelled), otherwise the unique index below cannot be built.
            migrationBuilder.Sql("""
                UPDATE "VisitRequests" AS v
                SET "Status" = 3
                FROM (
                    SELECT "Id",
                           row_number() OVER (PARTITION BY "PropertyId", "RequesterId"
                                              ORDER BY "CreatedAt", "Id") AS rn
                    FROM "VisitRequests"
                    WHERE "Status" IN (0, 5) AND NOT "IsDeleted"
                ) AS d
                WHERE v."Id" = d."Id" AND d.rn > 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_VisitRequests_PropertyId_RequesterId_Open",
                table: "VisitRequests",
                columns: new[] { "PropertyId", "RequesterId" },
                unique: true,
                filter: "\"Status\" IN (0, 5) AND NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_PropertyReviews_PropertyId_ReviewerId",
                table: "PropertyReviews",
                columns: new[] { "PropertyId", "ReviewerId" },
                unique: true,
                filter: "NOT \"IsDeleted\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Note: restoring the unfiltered review index fails if a user deleted a review and
            // wrote a new one for the same property after this migration. Roll back the API, not
            // this schema change (same advice as the other launch migrations).
            migrationBuilder.DropIndex(
                name: "IX_VisitRequests_PropertyId_RequesterId_Open",
                table: "VisitRequests");

            migrationBuilder.DropIndex(
                name: "IX_PropertyReviews_PropertyId_ReviewerId",
                table: "PropertyReviews");

            migrationBuilder.CreateIndex(
                name: "IX_PropertyReviews_PropertyId_ReviewerId",
                table: "PropertyReviews",
                columns: new[] { "PropertyId", "ReviewerId" },
                unique: true);
        }
    }
}
