using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SteamItems.Worker.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialWorker : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProcessedFiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Bucket = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    ETag = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ExportId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Error = table.Column<string>(type: "TEXT", nullable: true),
                    ProcessedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    FailedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CompletedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedFiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProcessedItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FileId = table.Column<int>(type: "INTEGER", nullable: false),
                    RowNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    AppId = table.Column<int>(type: "INTEGER", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Price = table.Column<decimal>(type: "TEXT", nullable: true),
                    ReleaseDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Error = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    RawAppId = table.Column<string>(type: "TEXT", nullable: true),
                    RawName = table.Column<string>(type: "TEXT", nullable: true),
                    RawPrice = table.Column<string>(type: "TEXT", nullable: true),
                    RawReleaseDate = table.Column<string>(type: "TEXT", nullable: true),
                    ProcessedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcessedItems_ProcessedFiles_FileId",
                        column: x => x.FileId,
                        principalTable: "ProcessedFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedFiles_Bucket_Key_ETag",
                table: "ProcessedFiles",
                columns: new[] { "Bucket", "Key", "ETag" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedItems_FileId_RowNumber",
                table: "ProcessedItems",
                columns: new[] { "FileId", "RowNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProcessedItems");

            migrationBuilder.DropTable(
                name: "ProcessedFiles");
        }
    }
}
