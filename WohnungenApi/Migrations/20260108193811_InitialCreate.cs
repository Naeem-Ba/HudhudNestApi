using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WohnungenApi.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "amenities",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_amenities", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "benutzer",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    displayname = table.Column<string>(type: "text", nullable: true),
                    isagent = table.Column<bool>(type: "boolean", nullable: true),
                    name = table.Column<string>(type: "text", nullable: true),
                    vorname = table.Column<string>(type: "text", nullable: true),
                    email = table.Column<string>(type: "text", nullable: false),
                    phone = table.Column<string>(type: "text", nullable: true),
                    passwordhash = table.Column<string>(type: "text", nullable: false),
                    taxnumber = table.Column<string>(type: "text", nullable: true),
                    role = table.Column<string>(type: "text", nullable: true),
                    imageurl = table.Column<string>(type: "text", nullable: true),
                    createdat = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    anzahle_wohnungen = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_benutzer", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "contactmessages",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    isread = table.Column<bool>(type: "boolean", nullable: false),
                    name = table.Column<string>(type: "text", nullable: true),
                    email = table.Column<string>(type: "text", nullable: true),
                    subject = table.Column<string>(type: "text", nullable: true),
                    message = table.Column<string>(type: "text", nullable: true),
                    sentat = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contactmessages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "wohnungen",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    titel = table.Column<string>(type: "text", nullable: false),
                    beschreibung = table.Column<string>(type: "text", nullable: false),
                    adresse = table.Column<string>(type: "text", nullable: false),
                    plz = table.Column<string>(type: "text", nullable: true),
                    latitude = table.Column<double>(type: "double precision", nullable: true),
                    longitude = table.Column<double>(type: "double precision", nullable: true),
                    zummieten = table.Column<bool>(type: "boolean", nullable: true),
                    zumkaufen = table.Column<bool>(type: "boolean", nullable: true),
                    kaltmiete = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    warmmiete = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    kaufpreis = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    nebenkosten = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    kaution = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    zimmer = table.Column<int>(type: "integer", nullable: true),
                    flaeche = table.Column<double>(type: "double precision", nullable: true),
                    geschoss = table.Column<int>(type: "integer", nullable: true),
                    freiab = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    balkon = table.Column<bool>(type: "boolean", nullable: true),
                    aufzug = table.Column<bool>(type: "boolean", nullable: true),
                    stellplatz = table.Column<bool>(type: "boolean", nullable: true),
                    heizung = table.Column<string>(type: "text", nullable: true),
                    stadt = table.Column<int>(type: "integer", nullable: true),
                    energieausweis = table.Column<int>(type: "integer", nullable: true),
                    zustand = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: true),
                    ownerid = table.Column<int>(type: "integer", nullable: true),
                    createdat = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updatedat = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wohnungen", x => x.id);
                    table.ForeignKey(
                        name: "FK_wohnungen_benutzer_ownerid",
                        column: x => x.ownerid,
                        principalTable: "benutzer",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "favorites",
                columns: table => new
                {
                    userid = table.Column<int>(type: "integer", nullable: false),
                    wohnungid = table.Column<int>(type: "integer", nullable: false),
                    createdat = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_favorites", x => new { x.userid, x.wohnungid });
                    table.ForeignKey(
                        name: "FK_favorites_benutzer_userid",
                        column: x => x.userid,
                        principalTable: "benutzer",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_favorites_wohnungen_wohnungid",
                        column: x => x.wohnungid,
                        principalTable: "wohnungen",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    wohnungid = table.Column<int>(type: "integer", nullable: false),
                    userid = table.Column<int>(type: "integer", nullable: true),
                    sendername = table.Column<string>(type: "text", nullable: true),
                    senderemail = table.Column<string>(type: "text", nullable: true),
                    message = table.Column<string>(type: "text", nullable: false),
                    createdat = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_messages", x => x.id);
                    table.ForeignKey(
                        name: "FK_messages_benutzer_userid",
                        column: x => x.userid,
                        principalTable: "benutzer",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_messages_wohnungen_wohnungid",
                        column: x => x.wohnungid,
                        principalTable: "wohnungen",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wohnungamenity",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    wohnungid = table.Column<int>(type: "integer", nullable: false),
                    amenityid = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wohnungamenity", x => x.id);
                    table.ForeignKey(
                        name: "FK_wohnungamenity_amenities_amenityid",
                        column: x => x.amenityid,
                        principalTable: "amenities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_wohnungamenity_wohnungen_wohnungid",
                        column: x => x.wohnungid,
                        principalTable: "wohnungen",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wohnungsamenities",
                columns: table => new
                {
                    amenitiesid = table.Column<int>(type: "integer", nullable: false),
                    wohnungenid = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wohnungsamenities", x => new { x.amenitiesid, x.wohnungenid });
                    table.ForeignKey(
                        name: "FK_wohnungsamenities_amenities_amenitiesid",
                        column: x => x.amenitiesid,
                        principalTable: "amenities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_wohnungsamenities_wohnungen_wohnungenid",
                        column: x => x.wohnungenid,
                        principalTable: "wohnungen",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wohnungsbilder",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    wohnungid = table.Column<int>(type: "integer", nullable: false),
                    url = table.Column<string>(type: "text", nullable: false),
                    ismain = table.Column<bool>(type: "boolean", nullable: false),
                    sortorder = table.Column<int>(type: "integer", nullable: true),
                    alttext = table.Column<string>(type: "text", nullable: true),
                    createdat = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wohnungsbilder", x => x.id);
                    table.ForeignKey(
                        name: "FK_wohnungsbilder_wohnungen_wohnungid",
                        column: x => x.wohnungid,
                        principalTable: "wohnungen",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_favorites_wohnungid",
                table: "favorites",
                column: "wohnungid");

            migrationBuilder.CreateIndex(
                name: "IX_messages_userid",
                table: "messages",
                column: "userid");

            migrationBuilder.CreateIndex(
                name: "IX_messages_wohnungid",
                table: "messages",
                column: "wohnungid");

            migrationBuilder.CreateIndex(
                name: "IX_wohnungamenity_amenityid",
                table: "wohnungamenity",
                column: "amenityid");

            migrationBuilder.CreateIndex(
                name: "IX_wohnungamenity_wohnungid",
                table: "wohnungamenity",
                column: "wohnungid");

            migrationBuilder.CreateIndex(
                name: "IX_wohnungen_ownerid",
                table: "wohnungen",
                column: "ownerid");

            migrationBuilder.CreateIndex(
                name: "IX_wohnungsamenities_wohnungenid",
                table: "wohnungsamenities",
                column: "wohnungenid");

            migrationBuilder.CreateIndex(
                name: "IX_wohnungsbilder_wohnungid",
                table: "wohnungsbilder",
                column: "wohnungid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contactmessages");

            migrationBuilder.DropTable(
                name: "favorites");

            migrationBuilder.DropTable(
                name: "messages");

            migrationBuilder.DropTable(
                name: "wohnungamenity");

            migrationBuilder.DropTable(
                name: "wohnungsamenities");

            migrationBuilder.DropTable(
                name: "wohnungsbilder");

            migrationBuilder.DropTable(
                name: "amenities");

            migrationBuilder.DropTable(
                name: "wohnungen");

            migrationBuilder.DropTable(
                name: "benutzer");
        }
    }
}
