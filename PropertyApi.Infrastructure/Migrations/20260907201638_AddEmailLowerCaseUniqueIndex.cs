using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropertyApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Finding F3 (docs/DATABASE-PRODUCTION-READINESS.md): the only unique index on email was
    /// `IX_Users_Email UNIQUE btree ("Email")` -- case-sensitive, on the raw column. Proven live
    /// against a real PostgreSQL database that two accounts differing only by email casing
    /// (e.g. "user@example.com" / "USER@example.com") could both be inserted whenever `UserName`
    /// is not also identical -- protection today is an incidental side effect of every current
    /// write path setting `UserName` equal to `Email`, not a declared database constraint.
    ///
    /// This migration is deliberately guarded rather than applied blindly: it raises and aborts
    /// (no row is modified, merged, or deleted) if any case-variant duplicate already exists,
    /// so a human must investigate before the constraint can be added. This is the "check
    /// current data before applying the constraint" step for an environment this session has no
    /// access to -- see docs/DATABASE-PRODUCTION-READINESS.md's Finding F3 update and
    /// scripts/database/audit-f4-numeric-constraints.sql's sibling note for the equivalent F4
    /// story. The count of colliding groups is logged, not the actual email values, so a
    /// migration failure does not leak PII into deployment logs.
    ///
    /// The pre-existing `IX_Users_Email` index is left in place -- removing it is a separate,
    /// unrelated decision (see this migration's own commit/PR description).
    /// </summary>
    public partial class AddEmailLowerCaseUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    duplicate_group_count integer;
                BEGIN
                    SELECT count(*) INTO duplicate_group_count
                    FROM (
                        SELECT lower("Email") AS normalized_email
                        FROM "Users"
                        WHERE "Email" IS NOT NULL
                        GROUP BY lower("Email")
                        HAVING count(*) > 1
                    ) AS colliding_groups;

                    IF duplicate_group_count > 0 THEN
                        RAISE EXCEPTION
                            'AddEmailLowerCaseUniqueIndex: % case-variant duplicate email group(s) '
                            'exist in "Users" (e.g. "user@example.com" and "USER@example.com" as '
                            'two separate accounts). This migration refuses to silently merge, '
                            'rename, or delete any account. Resolve every duplicate manually '
                            '(the SELECT above, run directly, names the colliding groups by '
                            'lower(Email) without exposing this in migration logs), then re-run '
                            'this migration.', duplicate_group_count;
                    END IF;
                END $$;
            """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX "IX_Users_Email_Lower" ON "Users" (lower("Email"));
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "IX_Users_Email_Lower";
            """);
        }
    }
}
