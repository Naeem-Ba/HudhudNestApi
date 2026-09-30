using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HudhudNestApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MakeShortStayCoordinatesNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "Longitude",
                table: "ShortStayListings",
                type: "numeric(9,6)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(9,6)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Latitude",
                table: "ShortStayListings",
                type: "numeric(9,6)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(9,6)");

            // 0,0 was the value every listing created before this change got (the create form had no
            // location fields). It is a point in the Atlantic, not a location: turn it into "no pin"
            // so guests no longer get a map link to nowhere. Listings stay published; the owner is
            // asked for a real location the next time they edit.
            migrationBuilder.Sql(
                "UPDATE \"ShortStayListings\" SET \"Latitude\" = NULL, \"Longitude\" = NULL " +
                "WHERE \"Latitude\" = 0 AND \"Longitude\" = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "Longitude",
                table: "ShortStayListings",
                type: "numeric(9,6)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(9,6)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Latitude",
                table: "ShortStayListings",
                type: "numeric(9,6)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(9,6)",
                oldNullable: true);
        }
    }
}
