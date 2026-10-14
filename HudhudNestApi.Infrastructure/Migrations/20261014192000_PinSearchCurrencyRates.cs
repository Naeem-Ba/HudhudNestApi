using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HudhudNestApi.Infrastructure.Migrations
{
    /// <summary>
    /// Pins the exchange rates of the three search currencies (US dollar, euro, Syrian pound) to
    /// the values of 2026-10-14 until a pricing feed replaces them. Data only: no schema change.
    /// Rows missing from the table are created. Rates an administrator sets afterwards through
    /// PUT /api/admin/currencies/{code} are kept, because a migration runs once.
    /// The Down path is empty: the previous values were placeholders and are not restored.
    ///
    /// SYP is expressed on the scale prices are advertised in today (about 13,400 pounds per
    /// dollar). The redenominated pound (two zeros removed, Jan 2026) would be 134; keep this in
    /// step with how listing prices are entered.
    /// </summary>
    public partial class PinSearchCurrencyRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "Currencies"
                SET "ExchangeRateToUSD" = v.rate, "ExchangeRateUpdatedAt" = now() at time zone 'utc', "IsActive" = TRUE
                FROM (VALUES ('USD', 1.000000::numeric), ('EUR', 0.892200::numeric), ('SYP', 13400.000000::numeric)) AS v(code, rate)
                WHERE trim("Currencies"."Code") = v.code;
                """);

            migrationBuilder.Sql("""
                INSERT INTO "Currencies" ("Code", "NameEn", "NameAr", "Symbol", "ExchangeRateToUSD", "ExchangeRateUpdatedAt", "IsBaseCurrency", "IsActive", "DecimalPlaces")
                SELECT v.code, v.name_en, v.name_ar, v.symbol, v.rate, now() at time zone 'utc', v.is_base, TRUE, v.places
                FROM (VALUES
                    ('USD', 'US Dollar', 'الدولار الأمريكي', '$', 1.000000::numeric, TRUE, 2),
                    ('EUR', 'Euro', 'اليورو', '€', 0.892200::numeric, FALSE, 2),
                    ('SYP', 'Syrian Pound', 'الليرة السورية', 'ل.س', 13400.000000::numeric, FALSE, 0)
                ) AS v(code, name_en, name_ar, symbol, rate, is_base, places)
                WHERE NOT EXISTS (SELECT 1 FROM "Currencies" c WHERE trim(c."Code") = v.code);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
