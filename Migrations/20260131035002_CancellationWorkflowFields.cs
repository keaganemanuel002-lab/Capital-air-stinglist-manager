using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StingListManager.Migrations
{
    /// <inheritdoc />
    public partial class CancellationWorkflowFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Processed",
                table: "CancellationEntries",
                newName: "Status");

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "CancellationEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "JobCardId",
                table: "CancellationEntries",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QuoteId",
                table: "CancellationEntries",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Code",
                table: "CancellationEntries");

            migrationBuilder.DropColumn(
                name: "JobCardId",
                table: "CancellationEntries");

            migrationBuilder.DropColumn(
                name: "QuoteId",
                table: "CancellationEntries");

            migrationBuilder.RenameColumn(
                name: "Status",
                table: "CancellationEntries",
                newName: "Processed");
        }
    }
}
