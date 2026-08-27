using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropertyApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionAndUserAccountXminConcurrencyToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Deliberately empty — same reasoning as 20260826073614_AddPropertyXminConcurrencyToken.
            // `xmin` is a PostgreSQL SYSTEM column present on every table already; the
            // scaffolded `AddColumn "xmin"` calls this migration started from would fail
            // outright, because "xmin" is a reserved column name Postgres will not let a user
            // column shadow. They have been removed for that reason.
            //
            // This migration exists only so the EF model snapshot picks up
            // TransactionConfiguration's and UserAccountConfiguration's new IsRowVersion()
            // mapping (RELEASE-BLOCKERS-AR.md B-9b) — there is no schema to change.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // See Up(): nothing was added, so there is nothing to remove. xmin stays,
            // because it is Postgres's own column, not this migration's.
        }
    }
}
