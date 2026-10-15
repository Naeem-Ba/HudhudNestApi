using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HudhudNestApi.Infrastructure.Migrations
{
    /// <summary>
    /// The Syrian pound is priced on the redenominated scale (two zeros removed on 1 January 2026,
    /// about 134 pounds per dollar), not the old scale (about 13,400) that PinSearchCurrencyRates
    /// pinned. Data only. A rate is replaced only while it is still on the old scale (above 1,000),
    /// so a value an administrator already set on the new scale is kept.
    /// The Down path is empty: the old-scale value was wrong for this product and is not restored.
    /// </summary>
    public partial class SetSypRateToRedenominatedScale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "Currencies"
                SET "ExchangeRateToUSD" = 134.000000, "ExchangeRateUpdatedAt" = now() at time zone 'utc'
                WHERE trim("Code") = 'SYP' AND "ExchangeRateToUSD" > 1000;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
