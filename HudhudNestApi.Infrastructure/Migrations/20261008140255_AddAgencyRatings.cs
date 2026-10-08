using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HudhudNestApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAgencyRatings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgencyRatings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AgencyId = table.Column<Guid>(type: "uuid", nullable: false),
                    RaterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Service = table.Column<int>(type: "integer", nullable: false),
                    Professionalism = table.Column<int>(type: "integer", nullable: false),
                    Reliability = table.Column<int>(type: "integer", nullable: false),
                    OverallExperience = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgencyRatings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgencyRatings_Agencies_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AgencyRatings_UserAccounts_RaterId",
                        column: x => x.RaterId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgencyRatings_AgencyId",
                table: "AgencyRatings",
                column: "AgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_AgencyRatings_AgencyId_RaterId",
                table: "AgencyRatings",
                columns: new[] { "AgencyId", "RaterId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgencyRatings_RaterId",
                table: "AgencyRatings",
                column: "RaterId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgencyRatings");
        }
    }
}
