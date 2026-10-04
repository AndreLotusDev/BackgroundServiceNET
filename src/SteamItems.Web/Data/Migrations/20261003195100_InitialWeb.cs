using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SteamItems.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialWeb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SteamItems",
                columns: table => new
                {
                    AppId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Price = table.Column<decimal>(type: "TEXT", nullable: false),
                    ReleaseDate = table.Column<DateOnly>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SteamItems", x => x.AppId);
                });

            migrationBuilder.CreateTable(
                name: "UserSelections",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    AppId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSelections", x => new { x.UserId, x.AppId });
                    table.ForeignKey(
                        name: "FK_UserSelections_SteamItems_AppId",
                        column: x => x.AppId,
                        principalTable: "SteamItems",
                        principalColumn: "AppId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "SteamItems",
                columns: new[] { "AppId", "Name", "Price", "ReleaseDate" },
                values: new object[,]
                {
                    { 70, "Half-Life", 9.99m, new DateOnly(1998, 11, 8) },
                    { 220, "Half-Life 2", 9.99m, new DateOnly(2004, 11, 16) },
                    { 400, "Portal", 9.99m, new DateOnly(2007, 10, 10) },
                    { 440, "Team Fortress 2", 0m, new DateOnly(2007, 10, 10) },
                    { 550, "Left 4 Dead 2", 9.99m, new DateOnly(2009, 11, 16) },
                    { 570, "Dota 2", 0m, new DateOnly(2013, 7, 9) },
                    { 620, "Portal 2", 9.99m, new DateOnly(2011, 4, 18) },
                    { 730, "Counter-Strike 2", 0m, new DateOnly(2012, 8, 21) },
                    { 105600, "Terraria", 9.99m, new DateOnly(2011, 5, 16) },
                    { 271590, "Grand Theft Auto V", 29.99m, new DateOnly(2015, 4, 14) },
                    { 292030, "The Witcher 3: Wild Hunt", 39.99m, new DateOnly(2015, 5, 18) },
                    { 367520, "Hollow Knight", 14.99m, new DateOnly(2017, 2, 24) },
                    { 413150, "Stardew Valley", 14.99m, new DateOnly(2016, 2, 26) },
                    { 489830, "The Elder Scrolls V: Skyrim Special Edition", 39.99m, new DateOnly(2016, 10, 27) },
                    { 646570, "Slay the Spire", 24.99m, new DateOnly(2019, 1, 23) },
                    { 892970, "Valheim", 19.99m, new DateOnly(2021, 2, 2) },
                    { 1086940, "Baldur's Gate 3", 59.99m, new DateOnly(2023, 8, 3) },
                    { 1091500, "Cyberpunk 2077", 59.99m, new DateOnly(2020, 12, 10) },
                    { 1145360, "Hades", 24.99m, new DateOnly(2020, 9, 17) },
                    { 1245620, "Elden Ring", 59.99m, new DateOnly(2022, 2, 24) }
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserSelections_AppId",
                table: "UserSelections",
                column: "AppId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserSelections");

            migrationBuilder.DropTable(
                name: "SteamItems");
        }
    }
}
