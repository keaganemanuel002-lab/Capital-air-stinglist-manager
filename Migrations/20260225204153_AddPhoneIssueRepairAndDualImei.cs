using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StingListManager.Migrations
{
    /// <inheritdoc />
    public partial class AddPhoneIssueRepairAndDualImei : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PhoneImeiSecondary",
                table: "PhoneIssueLogEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhoneImeiSecondaryNorm",
                table: "PhoneIssueLogEntries",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RepairDetails",
                table: "PhoneIssueLogEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PhoneIssueLogEntries_PhoneImeiSecondaryNorm",
                table: "PhoneIssueLogEntries",
                column: "PhoneImeiSecondaryNorm");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PhoneIssueLogEntries_PhoneImeiSecondaryNorm",
                table: "PhoneIssueLogEntries");

            migrationBuilder.DropColumn(
                name: "PhoneImeiSecondary",
                table: "PhoneIssueLogEntries");

            migrationBuilder.DropColumn(
                name: "PhoneImeiSecondaryNorm",
                table: "PhoneIssueLogEntries");

            migrationBuilder.DropColumn(
                name: "RepairDetails",
                table: "PhoneIssueLogEntries");
        }
    }
}
