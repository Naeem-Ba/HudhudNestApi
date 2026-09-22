using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HudhudNestApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPropertyXminConcurrencyToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Deliberately empty. `xmin` is a PostgreSQL SYSTEM column present on every
            // table already — `ALTER TABLE ... ADD COLUMN "xmin"` is not just unnecessary,
            // it fails outright, because "xmin" is a reserved column name Postgres will not
            // let a user column shadow. The scaffolded AddColumn call this migration started
            // from has been removed for that reason.
            //
            // This migration exists only so the EF model snapshot picks up
            // PropertyConfiguration's new IsRowVersion() mapping (RELEASE-BLOCKERS-AR.md
            // B-9) — there is no schema to change.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // See Up(): nothing was added, so there is nothing to remove. xmin stays,
            // because it is Postgres's own column, not this migration's.
        }
    }
}
