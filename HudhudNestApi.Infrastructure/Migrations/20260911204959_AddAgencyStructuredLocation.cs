using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HudhudNestApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAgencyStructuredLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DistrictId",
                table: "Agencies",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GovernorateId",
                table: "Agencies",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NeighborhoodId",
                table: "Agencies",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Agencies_DistrictId",
                table: "Agencies",
                column: "DistrictId");

            migrationBuilder.CreateIndex(
                name: "IX_Agencies_GovernorateId",
                table: "Agencies",
                column: "GovernorateId");

            migrationBuilder.CreateIndex(
                name: "IX_Agencies_NeighborhoodId",
                table: "Agencies",
                column: "NeighborhoodId");

            migrationBuilder.AddForeignKey(
                name: "FK_Agencies_Districts_DistrictId",
                table: "Agencies",
                column: "DistrictId",
                principalTable: "Districts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Agencies_Governorates_GovernorateId",
                table: "Agencies",
                column: "GovernorateId",
                principalTable: "Governorates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Agencies_Neighborhoods_NeighborhoodId",
                table: "Agencies",
                column: "NeighborhoodId",
                principalTable: "Neighborhoods",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Agencies_Districts_DistrictId",
                table: "Agencies");

            migrationBuilder.DropForeignKey(
                name: "FK_Agencies_Governorates_GovernorateId",
                table: "Agencies");

            migrationBuilder.DropForeignKey(
                name: "FK_Agencies_Neighborhoods_NeighborhoodId",
                table: "Agencies");

            migrationBuilder.DropIndex(
                name: "IX_Agencies_DistrictId",
                table: "Agencies");

            migrationBuilder.DropIndex(
                name: "IX_Agencies_GovernorateId",
                table: "Agencies");

            migrationBuilder.DropIndex(
                name: "IX_Agencies_NeighborhoodId",
                table: "Agencies");

            migrationBuilder.DropColumn(
                name: "DistrictId",
                table: "Agencies");

            migrationBuilder.DropColumn(
                name: "GovernorateId",
                table: "Agencies");

            migrationBuilder.DropColumn(
                name: "NeighborhoodId",
                table: "Agencies");
        }
    }
}
