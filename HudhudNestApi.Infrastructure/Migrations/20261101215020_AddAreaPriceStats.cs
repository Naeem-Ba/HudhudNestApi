using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HudhudNestApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAreaPriceStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AreaPriceStats",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    GovernorateId = table.Column<int>(type: "integer", nullable: false),
                    DistrictId = table.Column<int>(type: "integer", nullable: true),
                    PropertyTypeId = table.Column<int>(type: "integer", nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    SampleCount = table.Column<int>(type: "integer", nullable: false),
                    ExcludedCount = table.Column<int>(type: "integer", nullable: false),
                    P25PerSqmUsd = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    MedianPerSqmUsd = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    P75PerSqmUsd = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Confidence = table.Column<int>(type: "integer", nullable: false),
                    OldestListingAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    NewestListingAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ComputedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AreaPriceStats", x => x.Id);
                    table.CheckConstraint("CK_AreaPriceStats_Kind", "\"Kind\" IN (1, 2)");
                    table.CheckConstraint("CK_AreaPriceStats_Quartiles", "\"P25PerSqmUsd\" <= \"MedianPerSqmUsd\" AND \"MedianPerSqmUsd\" <= \"P75PerSqmUsd\"");
                    table.CheckConstraint("CK_AreaPriceStats_Sample", "\"SampleCount\" > 0");
                    table.ForeignKey(
                        name: "FK_AreaPriceStats_Districts_DistrictId",
                        column: x => x.DistrictId,
                        principalTable: "Districts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AreaPriceStats_Governorates_GovernorateId",
                        column: x => x.GovernorateId,
                        principalTable: "Governorates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AreaPriceStats_PropertyTypes_PropertyTypeId",
                        column: x => x.PropertyTypeId,
                        principalTable: "PropertyTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AreaPriceStats_DistrictId",
                table: "AreaPriceStats",
                column: "DistrictId");

            migrationBuilder.CreateIndex(
                name: "IX_AreaPriceStats_GovernorateId_Kind",
                table: "AreaPriceStats",
                columns: new[] { "GovernorateId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_AreaPriceStats_PropertyTypeId",
                table: "AreaPriceStats",
                column: "PropertyTypeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AreaPriceStats");
        }
    }
}
