using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HudhudNestApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHomeGovernorateAndPublicListCountIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "HomeGovernorateId",
                table: "UserAccounts",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserAccounts_HomeGovernorateId",
                table: "UserAccounts",
                column: "HomeGovernorateId");

            migrationBuilder.CreateIndex(
                name: "IX_Properties_PublicList_Count",
                table: "Properties",
                column: "ExpiresAt",
                filter: "NOT \"IsDeleted\" AND \"IsPublished\" AND \"Status\" IN ('Available', 'Reserved')")
                .Annotation("Npgsql:IndexInclude", new[] { "IsFeatured", "FeaturedUntil" });

            migrationBuilder.AddForeignKey(
                name: "FK_UserAccounts_Governorates_HomeGovernorateId",
                table: "UserAccounts",
                column: "HomeGovernorateId",
                principalTable: "Governorates",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserAccounts_Governorates_HomeGovernorateId",
                table: "UserAccounts");

            migrationBuilder.DropIndex(
                name: "IX_UserAccounts_HomeGovernorateId",
                table: "UserAccounts");

            migrationBuilder.DropIndex(
                name: "IX_Properties_PublicList_Count",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "HomeGovernorateId",
                table: "UserAccounts");
        }
    }
}
