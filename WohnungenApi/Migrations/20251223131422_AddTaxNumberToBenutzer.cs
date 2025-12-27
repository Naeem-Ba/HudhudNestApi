using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WohnungenApi.Migrations
{
    /// <inheritdoc />
    public partial class AddTaxNumberToBenutzer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TaxNumber",
                table: "Benutzer",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TaxNumber",
                table: "Benutzer");
        }
    }
}
