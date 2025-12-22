using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WohnungenApi.Migrations
{
    /// <inheritdoc />
    public partial class AddImageUrlToBenutzer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "Benutzer",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "Benutzer");
        }
    }
}
