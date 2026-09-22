using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HudhudNestApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRentalTermOwnershipAndAreaUnitToProperty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AreaUnit",
                table: "Properties",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "SquareMeter");

            migrationBuilder.AddColumn<string>(
                name: "RentalDurationType",
                table: "Properties",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "RentalEndDate",
                table: "Properties",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "RentalStartDate",
                table: "Properties",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AreaUnit",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "RentalDurationType",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "RentalEndDate",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "RentalStartDate",
                table: "Properties");
        }
    }
}
