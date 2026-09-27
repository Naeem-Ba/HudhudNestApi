using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropertyApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserRatingOptionalCriteria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Conduct",
                table: "UserRatings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InformationAccuracy",
                table: "UserRatings",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Conduct",
                table: "UserRatings");

            migrationBuilder.DropColumn(
                name: "InformationAccuracy",
                table: "UserRatings");
        }
    }
}
