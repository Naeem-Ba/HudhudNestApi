using HudhudNestApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HudhudNestApi.Infrastructure.Migrations
{
    /// <summary>
    /// Data repair for STG-001/STG-002. Soft-deleted PropertyImages kept IsMain=true and kept
    /// occupying IX_PropertyImages_OneMainPerProperty (unique on PropertyId WHERE IsMain), so those
    /// listings could neither promote nor upload an image (HTTP 500). The code now clears IsMain on
    /// delete; this clears the rows that were already left behind. No schema change.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20261008120000_ClearIsMainOnSoftDeletedPropertyImages")]
    public partial class ClearIsMainOnSoftDeletedPropertyImages : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE \"PropertyImages\" SET \"IsMain\" = FALSE WHERE \"IsDeleted\" = TRUE AND \"IsMain\" = TRUE;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Irreversible data repair: the previous IsMain value of deleted rows carries no meaning.
        }
    }
}
