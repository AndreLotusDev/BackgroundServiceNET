using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SteamItems.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExportOutcome : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CompletedAt",
                table: "Exports",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Error",
                table: "Exports",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FailedCount",
                table: "Exports",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ProcessedCount",
                table: "Exports",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Exports_Status",
                table: "Exports",
                column: "Status");

            // ExportStatus.Uploaded was renamed to Pending (task 11): the Worker has not reported on these yet.
            migrationBuilder.Sql("UPDATE Exports SET Status = 'Pending' WHERE Status = 'Uploaded';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Before task 11 every export was Uploaded.
            migrationBuilder.Sql("UPDATE Exports SET Status = 'Uploaded';");
            migrationBuilder.DropIndex(
                name: "IX_Exports_Status",
                table: "Exports");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "Exports");

            migrationBuilder.DropColumn(
                name: "Error",
                table: "Exports");

            migrationBuilder.DropColumn(
                name: "FailedCount",
                table: "Exports");

            migrationBuilder.DropColumn(
                name: "ProcessedCount",
                table: "Exports");
        }
    }
}
